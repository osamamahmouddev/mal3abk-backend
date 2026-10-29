#!/bin/sh
# Install Malabak Git Hooks
set -e

echo "📦 Configuring Malabak Git Hooks (.githooks)..."
chmod +x .githooks/pre-commit .githooks/commit-msg
git config core.hooksPath .githooks

echo "✅ Git hooks successfully configured to .githooks/"
