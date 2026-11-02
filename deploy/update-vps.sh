#!/bin/bash
# Pull and redeploy LinkSuite on the shared VPS. Builds one image at a time so Health Tutor keeps CPU headroom.
#   ssh healthtutor-vps 'nohup bash /opt/linksuite/deploy/update-vps.sh > /root/linksuite-build.log 2>&1 &'
#   ssh healthtutor-vps 'tail -f /root/linksuite-build.log'
# Pass service names to rebuild only those, e.g. `update-vps.sh api orders`.
cd /opt/linksuite || exit 1
F="-f docker-compose.vps.yml"
services=("$@")
[ ${#services[@]} -eq 0 ] && services=(api hub orders invoices book learn property school admin care)

echo "=== start $(date -Is)"
git pull --ff-only || { echo "=== FAILED git pull"; exit 1; }
git log --oneline -1
for s in "${services[@]}"; do
  echo "=== build $s $(date -Is)"
  docker compose $F build "$s" || { echo "=== FAILED $s"; exit 1; }
done
echo "=== build done $(date -Is)"
docker compose $F up -d
echo "=== up done $(date -Is)"
docker compose $F ps
for _ in $(seq 1 30); do curl -sf -o /dev/null https://api.linksuite.brosync.ng/health && break; sleep 2; done
curl -s -o /dev/null -w "api health %{http_code}\n" https://api.linksuite.brosync.ng/health
curl -s -o /dev/null -w "healthtutor health %{http_code}\n" https://healthtutor.api.brosync.ng/health
