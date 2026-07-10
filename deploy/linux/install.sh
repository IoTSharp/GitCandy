#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "Run this installer as root." >&2
  exit 1
fi

package_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
install_dir="/opt/gitcandy"
data_dir="/var/lib/gitcandy"

if ! getent group gitcandy >/dev/null; then
  groupadd --system gitcandy
fi

if ! id gitcandy >/dev/null 2>&1; then
  useradd --system --gid gitcandy --home-dir "${data_dir}" --shell /usr/sbin/nologin gitcandy
fi

systemctl stop gitcandy.service 2>/dev/null || true
install -d -m 0755 "${install_dir}"
install -d -o gitcandy -g gitcandy -m 0750 \
  "${data_dir}" \
  "${data_dir}/repositories" \
  "${data_dir}/cache" \
  "${data_dir}/logs"
install -d -o gitcandy -g gitcandy -m 0700 "${data_dir}/data-protection-keys"
cp -a "${package_dir}/app/." "${install_dir}/"

if [[ ! -f "${install_dir}/appsettings.Production.json" ]]; then
  install -m 0640 -o root -g gitcandy \
    "${package_dir}/appsettings.Production.json" \
    "${install_dir}/appsettings.Production.json"
fi

chown -R root:gitcandy "${install_dir}"
chmod 0755 "${install_dir}/GitCandy"
runuser -u gitcandy -- env ASPNETCORE_ENVIRONMENT=Production "${install_dir}/GitCandy" --migrate

install -m 0644 "${package_dir}/gitcandy.service" /etc/systemd/system/gitcandy.service
systemctl daemon-reload
systemctl enable --now gitcandy.service
systemctl --no-pager --full status gitcandy.service
