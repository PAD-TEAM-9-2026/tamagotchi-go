FROM postgres:17-bookworm@sha256:837bed18782820fe967067cb5328b02b0d9992c1be9a37ee0c9c492cfc03af3b
RUN apt-get update && apt-get install -y --no-install-recommends \
    python3-venv ca-certificates postgresql-17-postgis-3=3.6.4+dfsg-2.pgdg12+1 postgresql-17-postgis-3-scripts=3.6.4+dfsg-2.pgdg12+1 \
    && rm -rf /var/lib/apt/lists/* \
    && python3 -m venv /opt/patroni
COPY ha/requirements.txt /opt/ha/requirements.txt
RUN /opt/patroni/bin/pip install --no-cache-dir --require-hashes -r /opt/ha/requirements.txt
COPY ha/ /opt/ha/
COPY db/init-service-databases.sh /opt/ha/init-service-databases.sh
COPY db/user-management/001_init.sql /service-schema/user-management.sql
COPY db/battle/001_init.sql /service-schema/battle.sql
RUN chmod +x /opt/ha/entrypoint.sh /opt/ha/post-bootstrap.sh /opt/ha/init-service-databases.sh
ENV PATH="/opt/patroni/bin:${PATH}"
ENTRYPOINT ["/opt/ha/entrypoint.sh"]
