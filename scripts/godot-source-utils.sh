#!/usr/bin/env bash

apply_godot_patches() {
    local godot_dir="$1"
    local root="$2"
    local patch_dir="$root/patches/godot"

    if [ ! -d "$patch_dir" ]; then
        return 0
    fi

    python3 "$root/scripts/apply-godot-patches.py" "$godot_dir" "$patch_dir"
}
