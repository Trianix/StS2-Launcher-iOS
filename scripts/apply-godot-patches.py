#!/usr/bin/env python3
"""Apply an ordered patch stack without mistaking dependent patches for drift.

Build each valid prefix in a disposable Git index. Only patch-owned paths are
compared, so generated build outputs and unrelated local edits are preserved.
The real index and working files are untouched until a valid prefix is found.
"""
import os
from pathlib import Path
import subprocess
import sys
import tempfile


def apply_patches(source, patch_dir):
    source = Path(source).resolve()
    patches = sorted(Path(patch_dir).resolve().glob("*.patch"))
    if not patches:
        return

    def git(*args, env=None, data=None):
        result = subprocess.run(
            ["git", "-c", f"safe.directory={source.as_posix()}", "-C", str(source), *args],
            env=env, input=data, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
        )
        if result.returncode:
            raise RuntimeError(result.stderr.decode("utf-8", errors="replace").strip())
        return result.stdout

    def index_blobs(env):
        entries = {}
        for entry in git("ls-files", "--stage", "-z", env=env).split(b"\0"):
            if not entry:
                continue
            metadata, name = entry.split(b"\t", 1)
            _, blob, stage = metadata.split()
            if stage != b"0":
                raise RuntimeError("Unmerged entry in temporary patch index")
            entries[os.fsdecode(name)] = blob
        return entries

    with tempfile.TemporaryDirectory(prefix="godot-patch-index-") as folder:
        env = dict(os.environ, GIT_INDEX_FILE=str(Path(folder) / "index"))
        git("read-tree", "HEAD", env=env)
        states = [index_blobs(env)]
        for patch in patches:
            try:
                git("apply", "--cached", "--ignore-whitespace", str(patch), env=env)
            except RuntimeError as exc:
                raise RuntimeError(f"Godot patch stack does not apply to HEAD at {patch.name}: {exc}") from exc
            states.append(index_blobs(env))

        names = set().union(*(state.keys() for state in states))
        touched = sorted(name for name in names if any(state.get(name) != states[0].get(name) for state in states[1:]))
        current = {}
        for name in touched:
            path = source / name
            if path.is_symlink():
                current[name] = git("hash-object", "--stdin", data=os.fsencode(os.readlink(path))).strip()
            elif path.is_file():
                # Apply the checkout's normal text filters, including CRLF.
                current[name] = git("hash-object", f"--path={name}", "--", name).strip()
            elif not path.exists():
                current[name] = None
            else:
                raise RuntimeError(f"Expected a patch-owned file at {path}")

        prefixes = [index for index, state in enumerate(states)
                    if all(current[name] == state.get(name) for name in touched)]
        if not prefixes:
            mismatches = [name for name in touched if current[name] != states[-1].get(name)]
            raise RuntimeError("Godot source is not a valid prefix of the current patch stack. "
                               "Preserve local edits or use a clean pinned checkout. Differing paths: "
                               + ", ".join(mismatches))
        prefix = prefixes[-1]
        if prefix == len(patches):
            print(f"Godot patch stack already applied and verified ({len(patches)} patches).")
            return
        for patch in patches[prefix:]:
            print(f"Applying Godot patch: {patch.name}", flush=True)
            git("apply", "--ignore-whitespace", str(patch))


if __name__ == "__main__":
    try:
        apply_patches(sys.argv[1], sys.argv[2])
    except (RuntimeError, OSError) as exc:
        raise SystemExit(f"ERROR: {exc}")
