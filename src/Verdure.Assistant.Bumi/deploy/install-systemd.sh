#!/bin/bash
# 把 Verdure.Assistant.Bumi 装成开机自启的 systemd 服务。
# 用法（需要 root）：sudo bash <发布目录>/deploy/install-systemd.sh
set -euo pipefail

unit_name=verdure-assistant-bumi.service
here=$(cd "$(dirname "$0")" && pwd)
app_dir=$(dirname "$here")
unit_src="$here/$unit_name"

# 已经用 sudo 跑时不要再套一层 sudo，否则会再次要求密码而卡住/失败
if [ "$(id -u)" -eq 0 ]; then
  SUDO=""
else
  SUDO="sudo"
fi

if [ ! -f "$unit_src" ]; then
  echo "找不到单元文件: $unit_src" >&2
  exit 1
fi

if [ ! -x "$app_dir/Verdure.Assistant.Bumi" ]; then
  echo "找不到可执行文件: $app_dir/Verdure.Assistant.Bumi（确认发布产物完整且已 chmod +x）" >&2
  exit 1
fi

# 单元里写死了 WorkingDirectory / 可执行文件路径，应用目录不是 /home/noetix/bumi-api 时提示
if ! grep -q "WorkingDirectory=$app_dir" "$unit_src"; then
  echo "警告：单元文件里的 WorkingDirectory 与实际目录 $app_dir 不一致，请先编辑 $unit_src" >&2
fi

echo "==> 安装 $unit_name（WorkingDirectory=$app_dir）"
$SUDO cp "$unit_src" "/etc/systemd/system/$unit_name"
$SUDO systemctl daemon-reload
$SUDO systemctl enable "$unit_name"
$SUDO systemctl restart "$unit_name"

sleep 3
systemctl --no-pager --lines=15 status "$unit_name" || true

cat <<'EOF'

==> 安装完成。常用命令：
  systemctl status verdure-assistant-bumi      # 看状态
  journalctl -u verdure-assistant-bumi -f      # 跟踪日志
  sudo systemctl restart verdure-assistant-bumi
  sudo systemctl disable --now verdure-assistant-bumi   # 停用并取消开机自启

注意：停止服务时会恢复机器人的音频开关；若强制 kill -9，开关会停留在禁用状态。
EOF
