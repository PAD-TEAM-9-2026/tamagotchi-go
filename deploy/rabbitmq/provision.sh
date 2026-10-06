#!/bin/sh
# Creates one RabbitMQ account per publisher or consumer. Each account can configure, write
# and read only its own exchanges and queues. Runs once, after the broker is healthy.
set -eu

ADMIN="rabbitmqadmin --host=rabbitmq --port=15672 --username=${RABBITMQ_USER} --password=${RABBITMQ_PASSWORD}"

until $ADMIN list vhosts >/dev/null 2>&1; do
  echo "waiting for rabbitmq"
  sleep 2
done

# account <name> <password> <configure> <write> <read>
account() {
  $ADMIN declare user name="$1" password="$2" tags=""
  $ADMIN declare permission vhost=/ user="$1" configure="$3" write="$4" read="$5"
}

account guild "${GUILD_RABBITMQ_PASSWORD}" \
  '^guild\.events$' \
  '^guild\.events$' \
  '^$'

account registry "${REGISTRY_RABBITMQ_PASSWORD}" \
  '^(package-registry\.events|package-registry\.(work|retry\.5s|retry\.30s|dlq))$' \
  '^(package-registry\.events|package-registry\.(work|retry\.5s|retry\.30s|dlq))$' \
  '^(package-registry\.work|user-management\.events)$'

echo "rabbitmq accounts ready"
