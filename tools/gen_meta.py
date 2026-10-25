#!/usr/bin/env python3
"""パッケージ内のファイルに .meta を作る（既にあるものはそのまま）。

GUID はパッケージ内の相対パスから決まるので、何度実行しても同じになる。
バックアップ（.asset）はスクリプトを GUID で参照するため、GUID を固定しておく必要がある。

使い方: python3 tools/gen_meta.py
"""
import hashlib
import os
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PACKAGE = os.path.join(REPO, "Packages", "com.suzuki-y0.jeni.kaihen-navi")
SEED = "com.suzuki-y0.jeni.kaihen-navi/"

COMMON_TAIL = "  userData: \n  assetBundleName: \n  assetBundleVariant: \n"

TEMPLATES = {
    "folder": "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n" + COMMON_TAIL,
    ".shader": "ShaderImporter:\n  externalObjects: {}\n  defaultTextures: []\n  nonModifiableTextures: []\n" + COMMON_TAIL,
    ".cs": (
        "MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n"
        "  executionOrder: 0\n  icon: {instanceID: 0}\n" + COMMON_TAIL
    ),
    ".asmdef": "AssemblyDefinitionImporter:\n  externalObjects: {}\n" + COMMON_TAIL,
    ".json": "TextScriptImporter:\n  externalObjects: {}\n" + COMMON_TAIL,
    ".md": "TextScriptImporter:\n  externalObjects: {}\n" + COMMON_TAIL,
    ".txt": "TextScriptImporter:\n  externalObjects: {}\n" + COMMON_TAIL,
    ".ttf": (
        "TrueTypeFontImporter:\n  externalObjects: {}\n  serializedVersion: 4\n  fontSize: 16\n"
        "  forceTextureCase: -2\n  characterSpacing: 0\n  characterPadding: 1\n  includeFontData: 1\n"
        "  fontNames: []\n  fallbackFontReferences: []\n  customCharacters: \n  fontRenderingMode: 0\n"
        "  ascentCalculationMode: 1\n  useLegacyBoundsCalculation: 0\n  shouldRoundAdvanceValue: 1\n"
        + COMMON_TAIL
    ),
    ".uss": (
        "ScriptedImporter:\n  internalIDToNameTable: []\n  externalObjects: {}\n  serializedVersion: 2\n"
        + COMMON_TAIL
        + "  script: {fileID: 12385, guid: 0000000000000000e000000000000000, type: 0}\n"
        "  disableValidation: 0\n"
    ),
}

PACKAGE_MANIFEST = "PackageManifestImporter:\n  externalObjects: {}\n" + COMMON_TAIL


def guid_for(rel):
    return hashlib.md5((SEED + rel).encode("utf-8")).hexdigest()


def body_for(path, rel):
    if os.path.isdir(path):
        return TEMPLATES["folder"]
    if rel == "package.json":
        return PACKAGE_MANIFEST
    ext = os.path.splitext(path)[1].lower()
    return TEMPLATES.get(ext)


def main():
    created = 0
    for dirpath, dirnames, filenames in os.walk(PACKAGE):
        dirnames[:] = [d for d in dirnames if not d.startswith(".")]
        entries = [os.path.join(dirpath, d) for d in dirnames] + [
            os.path.join(dirpath, f) for f in filenames if not f.endswith(".meta") and not f.startswith(".")
        ]
        for path in entries:
            meta = path + ".meta"
            if os.path.exists(meta):
                continue
            rel = os.path.relpath(path, PACKAGE).replace(os.sep, "/")
            body = body_for(path, rel)
            if body is None:
                print("skip (unknown type):", rel, file=sys.stderr)
                continue
            with open(meta, "w", encoding="utf-8", newline="\n") as f:
                f.write("fileFormatVersion: 2\nguid: %s\n%s" % (guid_for(rel), body))
            created += 1
    print("created %d .meta files" % created)


if __name__ == "__main__":
    main()
