// Two-replica Guild chat through the message broker. Runs inside a Guild container.
// Real paths: Guild HTTP API, chat tickets, WebSocket chat, Guild -> broker publish,
// broker fan-out to per-replica queues, ServiceDelivery.Deliver, history endpoint.
// Fixture: the guild and its two members are inserted into Guild's database, because
// creating a guild and inviting call User Management through the Gateway, which this
// subset does not run.
//
// From deploy/, with the candidate running (compose.yaml + compose.broker.yaml):
//   docker compose -f compose.yaml -f compose.broker.yaml exec -T \
//     -e GATEWAY_KEY="$(cat secrets/gateway-assertion.pem)" \
//     -e GATEWAY_KID="$(grep '^GATEWAY_ASSERTION_KEY_ID=' .env | cut -d= -f2-)" \
//     --index 1 guild node --input-type=module - < message-broker/checks/guild-relay.mjs
// Passes (exit 0) when the other replica receives the message once and it is in history.
import { createSign, randomUUID } from 'node:crypto';
import pg from 'pg';
import WebSocket from 'ws';

const key = process.env.GATEWAY_KEY;
const kid = process.env.GATEWAY_KID;
const b64 = (v) => Buffer.from(JSON.stringify(v)).toString('base64url');
function assertion(userId) {
  const iat = Math.floor(Date.now() / 1000);
  const input = `${b64({ alg: 'RS256', typ: 'gateway-assertion+jwt', kid })}.${b64({
    iss: 'tamagotchi-go-gateway', aud: 'guild', principal_kind: 'user', sub: userId, roles: [], scope: '',
    jti: randomUUID(), correlation_id: randomUUID(), iat, exp: iat + 30, deadline_unix_ms: (iat + 5) * 1000,
  })}`;
  return `${input}.${createSign('RSA-SHA256').update(input).sign(key).toString('base64url')}`;
}
async function http(base, method, path, userId, body) {
  const headers = { 'x-gateway-assertion': assertion(userId), 'content-type': 'application/json', 'idempotency-key': randomUUID() };
  const response = await fetch(`${base}${path}`, { method, headers, body: body ? JSON.stringify(body) : undefined });
  const text = await response.text();
  if (!response.ok) throw new Error(`${method} ${path} -> ${response.status} ${text}`);
  return text ? JSON.parse(text) : null;
}
function connect(base, guildId, ticket, label) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(`${base.replace('http', 'ws')}/v1/guilds/${guildId}/chat`);
    const frames = [];
    ws.on('message', (data) => frames.push(JSON.parse(data.toString())));
    ws.on('open', () => ws.send(JSON.stringify({ type: 'auth', ticket })));
    ws.on('error', reject);
    const started = Date.now();
    const timer = setInterval(() => {
      if (frames.some((f) => f.type === 'ready')) { clearInterval(timer); resolve({ ws, frames, label }); }
      else if (Date.now() - started > 5000) { clearInterval(timer); reject(new Error(`${label}: no ready frame: ${JSON.stringify(frames)}`)); }
    }, 50);
  });
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const discovery = await (await fetch('http://service-discovery:3009/v1/services/guild/instances')).json();
const replicas = discovery.instances.map((i) => ({ id: i.instance_id, base: `http://${i.host}:${i.port}`, grpc: i.grpc_endpoint, subs: i.subscriptions }));
console.log('replicas', JSON.stringify(replicas));
if (replicas.length < 2) throw new Error('need two Guild replicas');
const [a, b] = replicas;

const leader = randomUUID();
const member = randomUUID();
const guild = { guild_id: randomUUID() };
const db = new pg.Client({ host: process.env.DB_HOST, port: Number(process.env.DB_PORT), user: process.env.POSTGRES_USER, password: process.env.POSTGRES_PASSWORD, database: process.env.POSTGRES_DB });
await db.connect();
await db.query('INSERT INTO guilds (guild_id, name, description, leader_id) VALUES ($1, $2, $3, $4)', [guild.guild_id, `Relay ${Date.now()}`, 'broker relay check', leader]);
await db.query("INSERT INTO guild_members (guild_id, user_id, role) VALUES ($1, $2, 'LEADER'), ($1, $3, 'MEMBER')", [guild.guild_id, leader, member]);
await db.end();

const leaderTicket = await http(a.base, 'POST', `/v1/guilds/${guild.guild_id}/chat-tickets`, leader);
const memberTicket = await http(b.base, 'POST', `/v1/guilds/${guild.guild_id}/chat-tickets`, member);
const onA = await connect(a.base, guild.guild_id, leaderTicket.ticket, 'leader@A');
const onB = await connect(b.base, guild.guild_id, memberTicket.ticket, 'member@B');

const content = `hello across replicas ${Date.now()}`;
const sentAt = Date.now();
onA.ws.send(JSON.stringify({ type: 'send', client_message_id: randomUUID(), content }));
let relayedAfterMs;
while (Date.now() - sentAt < 15000) {
  if (onB.frames.some((f) => f.type === 'message' && f.message?.content === content)) { relayedAfterMs = Date.now() - sentAt; break; }
  await sleep(50);
}
await sleep(3000);
const copiesOnB = onB.frames.filter((f) => f.type === 'message' && f.message?.content === content).length;
const copiesOnA = onA.frames.filter((f) => f.type === 'message' && f.message?.content === content).length;
const history = await http(b.base, 'GET', `/v1/guilds/${guild.guild_id}/messages?limit=10`, member);
const inHistory = history.items.some((m) => m.content === content);

// Ticket replay stays refused: the leader's used ticket opens nothing.
const replay = await connect(a.base, guild.guild_id, leaderTicket.ticket, 'replay').then(() => 'accepted', (e) => `refused (${String(e.message).slice(0, 80)})`);

console.log(JSON.stringify({ guild_id: guild.guild_id, relayed_to_other_replica_ms: relayedAfterMs ?? null, copies_on_other_replica: copiesOnB, copies_on_sender_replica: copiesOnA, in_history: inHistory, ticket_replay: replay }));
onA.ws.close();
onB.ws.close();
process.exit(relayedAfterMs !== undefined && copiesOnB === 1 && inHistory ? 0 : 1);
