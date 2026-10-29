#!/usr/bin/env bash
# ==============================================================================
# Malabak Origin Firewall Sync Script
# Locks Linux firewall (UFW) to Cloudflare IPv4 & IPv6 ranges exclusively.
# Prevents direct-to-IP scanning and bypassing Cloudflare WAF/DDoS protection.
# ==============================================================================

set -euo pipefail

echo "=== [Malabak Origin Firewall Sync] Fetching Cloudflare IP ranges ==="

CF_IPV4_URL="https://www.cloudflare.com/ips-v4"
CF_IPV6_URL="https://www.cloudflare.com/ips-v6"

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "${TMP_DIR}"' EXIT

curl -sSfL "${CF_IPV4_URL}" -o "${TMP_DIR}/ips-v4.txt"
curl -sSfL "${CF_IPV6_URL}" -o "${TMP_DIR}/ips-v6.txt"

echo "Fetched $(wc -l < "${TMP_DIR}/ips-v4.txt") IPv4 ranges and $(wc -l < "${TMP_DIR}/ips-v6.txt") IPv6 ranges."

if command -v ufw >/dev/null 2>&1; then
    echo "=== Updating UFW Rules ==="
    # Reset existing cloudflare-specific rules if managed by tag
    while IFS= read -r ip; do
        [[ -z "${ip}" ]] && continue
        ufw allow proto tcp from "${ip}" to any port 443 comment "Cloudflare Edge Ingress"
    done < "${TMP_DIR}/ips-v4.txt"

    while IFS= read -r ip; do
        [[ -z "${ip}" ]] && continue
        ufw allow proto tcp from "${ip}" to any port 443 comment "Cloudflare Edge Ingress IPv6"
    done < "${TMP_DIR}/ips-v6.txt"

    echo "✅ UFW rules updated. Direct ingress on port 443 restricted to Cloudflare."
else
    echo "⚠️ UFW not found. Generating iptables rule snapshot to ${TMP_DIR}/iptables-cf.rules"
    {
        echo "*filter"
        echo ":CLOUDFLARE - [0:0]"
        while IFS= read -r ip; do
            [[ -z "${ip}" ]] && continue
            echo "-A CLOUDFLARE -p tcp -s ${ip} --dport 443 -j ACCEPT"
        done < "${TMP_DIR}/ips-v4.txt"
        echo "-A CLOUDFLARE -p tcp --dport 443 -j DROP"
        echo "COMMIT"
    } > "${TMP_DIR}/iptables-cf.rules"
    echo "✅ Generated iptables rules at ${TMP_DIR}/iptables-cf.rules"
fi

echo "=== Firewall sync completed successfully ==="
