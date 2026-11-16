#!/usr/bin/env bash
# Bash script to install .NET 10 SDK (no admin required)
# Usage: bash ./scripts/install-dotnet.sh

INSTALL_DIR="$HOME/.dotnet"
CHANNEL="10.0"

set -e

echo "Downloading dotnet-install script..."
curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
chmod +x dotnet-install.sh

echo "Installing .NET $CHANNEL SDK to $INSTALL_DIR (no admin)..."
./dotnet-install.sh --channel $CHANNEL --install-dir "$INSTALL_DIR"

echo "Installation completed. Add the following to your shell profile (e.g. ~/.bashrc):"
echo "  export PATH=\"$INSTALL_DIR:$PATH\""
echo "Then run: source ~/.bashrc (or open a new terminal)."

./$INSTALL_DIR/dotnet --info || true
