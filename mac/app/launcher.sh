#!/bin/bash
# .app entry point: opens the menu in Terminal so progress and prompts are visible.
RES="$(cd "$(dirname "$0")/../Resources" && pwd)"
exec /usr/bin/open -a Terminal "$RES/menu.command"
