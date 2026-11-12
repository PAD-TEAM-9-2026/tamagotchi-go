// Publishes one user.package_joined.v1 to the broker with the user-management workload
// certificate, as User Management will once its broker switch is on. Prints the event.
//
// From deploy/, with the candidate running:
//   docker compose -f compose.yaml -f compose.broker.yaml run --rm --no-deps -T \
//     -v "$PWD/secrets/tls/ca.crt:/certs/ca.crt:ro" \
//     -v "$PWD/secrets/tls/user-management.crt:/certs/um.crt:ro" \
//     -v "$PWD/secrets/tls/user-management.key:/certs/um.key:ro" \
//     --entrypoint node registry --input-type=module - < message-broker/checks/package-joined.mjs
// Then Registry should hold the printed user_id:
//   docker compose -f compose.yaml -f compose.broker.yaml exec -T database \
//     psql -U postgres -d registry -c "SELECT * FROM membership_projection WHERE user_id='<user_id>'"
import { readFileSync } from 'node:fs';
import grpc from '@grpc/grpc-js';
import protoLoader from '@grpc/proto-loader';
import { v7 as uuidv7 } from 'uuid';

const definition = protoLoader.loadSync('/app/contracts/broker.proto', { keepCase: true, longs: String, enums: String, defaults: true, oneofs: true });
const { MessageBroker } = grpc.loadPackageDefinition(definition).tamagotchi.messaging.v1;
const credentials = grpc.credentials.createSsl(readFileSync('/certs/ca.crt'), readFileSync('/certs/um.key'), readFileSync('/certs/um.crt'));
const client = new MessageBroker('message-broker:5010', credentials);

const event = { user_id: uuidv7(), package_ids: [process.env.PACKAGE_ID ?? uuidv7()], membership_version: 1 };
const request = {
  topic: 'user.package_joined.v1',
  event: {
    event_id: uuidv7(),
    event_type: 'user.package_joined.v1',
    occurred_at: { seconds: String(Math.floor(Date.now() / 1000)), nanos: 0 },
    producer: 'user-management',
    correlation_id: uuidv7(),
    data_json: Buffer.from(JSON.stringify(event)),
  },
};
client.Publish(request, { deadline: Date.now() + 5000 }, (error, receipt) => {
  if (error) { console.error(`publish failed: ${error.code} ${error.details}`); process.exit(1); }
  console.log(JSON.stringify({ event_id: receipt.event_id, user_id: event.user_id, package_ids: event.package_ids }));
  client.close();
});
