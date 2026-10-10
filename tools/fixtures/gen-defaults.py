#!/usr/bin/env python3
"""Generates uno/src/Soncle.Core/Store/Defaults.g.cs from fixtures/store/defaults.json."""
import json
import pathlib

root = pathlib.Path("/workspace/project/Soncle-music")
data = json.loads((root / "fixtures/store/defaults.json").read_text())
compact = json.dumps(data, separators=(",", ":"))
out = root / "uno/src/Soncle.Core/Store/Defaults.g.cs"
out.write_text(
    "// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later\n"
    "// Generated from fixtures/store/defaults.json (which comes from src/defaults.mjs).\n"
    "// Do not edit by hand: run tools/fixtures/export-store.mjs then tools/fixtures/gen-defaults.mjs.\n"
    "namespace Soncle.Core.Store;\n\n"
    "internal static class DefaultsData\n{\n"
    '    internal const string Json = """' + compact + '""";\n'
    "}\n"
)
print("wrote", out, len(compact), "bytes")
