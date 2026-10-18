#!/usr/bin/env bash
# Builds the GHDL plugin for the yosys of a Windows OSS CAD Suite release and packs it with the GHDL
# std/ieee libraries into ghdl-plugin-win-x64-<oss version>.zip, the asset the extension downloads.
# Also packs the corresponding source into ghdl-plugin-src-<oss version>.tar.gz, to publish next to it.
#
# The Windows OSS CAD Suite ships yosys without GHDL and its plugin (the Linux and macOS builds include both).
# A yosys plugin has to match the yosys it is loaded into, so the plugin is built per OSS CAD Suite release.
# GHDL is built from source at the given commit, so it can be the same GHDL the Linux OSS CAD Suite ships
# (bin/ghdl --version there, e.g. 6.0.0.r375.g3815871cd -> 3815871cd).
#
# Needs MSYS2 with: mingw-w64-x86_64-gcc mingw-w64-x86_64-gcc-ada make diffutils git
# (mingw64, not ucrt64: OSS CAD Suite is built against msvcrt)
#
# Usage: build.sh <oss-cad-suite folder> <ghdl commit> <ghdl-yosys-plugin commit> [output folder]
#   e.g. build.sh /c/Users/me/OneWareStudio/Packages/NativeTools/osscadsuite/oss-cad-suite 3815871cd a45a8fb out

set -euo pipefail

USAGE="Usage: build.sh <oss-cad-suite folder> <ghdl commit> <ghdl-yosys-plugin commit> [output folder]"
GHDL_REPO=https://github.com/ghdl/ghdl.git
PLUGIN_REPO=https://github.com/ghdl/ghdl-yosys-plugin.git

MINGW=${MINGW:-/c/msys64/mingw64}
SCRIPT_DIR=$(cd "$(dirname "$0")" && pwd)

OSS=${1:?$USAGE}
GHDL_REF=${2:?$USAGE}
PLUGIN_REF=${3:?$USAGE}
OSS=$(cd "$OSS" && pwd)
OUT=$(mkdir -p "${4:-out}" && cd "${4:-out}" && pwd)
WORK="$OUT/work"

export PATH="$MINGW/bin:$PATH"

OSS_VERSION=$(tr -d '[:space:]' < "$OSS/VERSION")
YOSYS_EXE="$OSS/bin/yosys.exe"
YOSYS_INCLUDE="$OSS/share/yosys/include"
# Compiler flags yosys was built with: -std and the defines of the --cxxflags line of yosys-config, e.g.
# -std=c++20 -D_YOSYS_ -DYOSYS_VER="0.68+71" ... (its -I paths are of the build machine). Older releases
# write the quotes as \" and have more defines, like -DYOSYS_ENABLE_TCL
YOSYS_CXXFLAGS=$(grep -m1 -e '--cxxflags ' "$OSS/bin/yosys-config" |
    grep -oE -e "-std=[^ ']+|-D[A-Za-z0-9_]+(=[^ ']+)?" | sed 's/\\"/"/g' | tr '\n' ' ' || true)
if [[ "$YOSYS_CXXFLAGS" != *-D_YOSYS_* ]]; then
    echo "Could not read the compiler flags from $OSS/bin/yosys-config" >&2
    exit 1
fi

rm -rf "$WORK" && mkdir -p "$WORK"
cd "$WORK"

# Sources. GHDL's testsuite has paths longer than Windows allows by default.
# Line endings stay LF, the patches are LF (git on Windows CI runners converts to CRLF by default)
git clone -q --no-checkout -c core.longpaths=true -c core.autocrlf=false "$GHDL_REPO" ghdl-src
git -C ghdl-src checkout -q "$GHDL_REF"
git -C ghdl-src apply "$SCRIPT_DIR/ghdl-ar-response-file.patch"
GHDL_COMMIT=$(git -C ghdl-src rev-parse --short=9 HEAD)
GHDL_DESCRIBE=$(git -C ghdl-src describe --tags)

git clone -q -c core.autocrlf=false "$PLUGIN_REPO" plugin
git -C plugin checkout -q "$PLUGIN_REF"
# Makes the plugin find the GHDL libraries next to itself
git -C plugin apply "$SCRIPT_DIR/default-prefix.patch"
PLUGIN_COMMIT=$(git -C plugin rev-parse --short=7 HEAD)

echo "OSS CAD Suite $OSS_VERSION, GHDL $GHDL_DESCRIBE, ghdl-yosys-plugin $PLUGIN_COMMIT"
echo "yosys flags: $YOSYS_CXXFLAGS"

# GHDL with libghdl (static libghdl.a included) and the precompiled std/ieee libraries
GHDL_ROOT="$WORK/ghdl"
mkdir ghdl-build
(cd ghdl-build && ../ghdl-src/configure --prefix="$GHDL_ROOT" --enable-libghdl --enable-synth && make -j"$(nproc)" && make install)
GHDL_LIB="$GHDL_ROOT/lib/ghdl"

# OSS CAD Suite has no import library for yosys.exe, so build one from its exports.
# Exports outside .text are variables and must be marked DATA, or the plugin reads a jump stub instead.
objdump -h "$YOSYS_EXE" > sections.txt
objdump -p "$YOSYS_EXE" > exports.txt
IMAGE_BASE=$(awk '/^ImageBase/{print $2; exit}' exports.txt)
read -r TEXT_VMA TEXT_SIZE < <(awk '$2==".text"{print $4, $3; exit}' sections.txt)
{
    echo "NAME yosys.exe"
    echo "EXPORTS"
    awk -v base="0x$IMAGE_BASE" -v vma="0x$TEXT_VMA" -v size="0x$TEXT_SIZE" '
        BEGIN { start = strtonum(vma) - strtonum(base); end = start + strtonum(size) }
        /^Export Address Table -- Ordinal Base/ { m = 1; next }
        /^\[Ordinal\/Name Pointer\] Table/ { m = 2; next }
        m == 1 && /^\t\[/ { gsub(/[\[\]]/, " "); rva[$1] = strtonum("0x" $4); next }
        m == 2 && /^\t\[/ {
            r = rva[strtonum("0x" $(NF - 1))]
            print "\"" $NF "\"" ((r >= start && r < end) ? "" : " DATA")
        }' exports.txt
} > yosys.def
dlltool -d yosys.def -l libyosys_exe.a -D yosys.exe

# libghdl and the GNAT runtime are linked statically, so the plugin only needs yosys.exe and libstdc++
# (both part of OSS CAD Suite). GHDL was built with this gcc, so its GNAT runtime is the matching one
# shellcheck disable=SC2086
g++ $YOSYS_CXXFLAGS -O2 -DYOSYS_ENABLE_GHDL -DGHDL_VER_HASH="\"$PLUGIN_COMMIT\"" \
    -I"$YOSYS_INCLUDE" -I"$GHDL_ROOT/include" -c plugin/src/ghdl.cc -o ghdl.o
GNAT_LIB=$(dirname "$(g++ -print-libgcc-file-name)")/adalib/libgnat.a
g++ -shared -o ghdl.so ghdl.o -L. -lyosys_exe "$GHDL_ROOT/lib/libghdl.a" "$GNAT_LIB" -static-libgcc

# Package: ghdl.so, lib/ (VHDL-2008 std and ieee, plus the sources they refer to) and the notices
PKG="$WORK/pkg"
mkdir -p "$PKG/lib/std" "$PKG/lib/ieee"
cp ghdl.so "$PKG/"
cp -r "$GHDL_LIB/std/v08" "$PKG/lib/std/"
cp -r "$GHDL_LIB/ieee/v08" "$PKG/lib/ieee/"
cp -r "$GHDL_LIB/src" "$PKG/lib/"
cp plugin/LICENSE "$PKG/LICENSE-ghdl-yosys-plugin.txt"
cp ghdl-src/COPYING.md "$PKG/LICENSE-ghdl.md"
GCC_VERSION=$(g++ -dumpfullversion)
SRC_NAME="ghdl-plugin-src-$OSS_VERSION"
cat > "$PKG/NOTICE.txt" <<EOF
GHDL plugin for the yosys of OSS CAD Suite $OSS_VERSION (Windows x64), used by the OneWare PSL Extension.
Built by tools/ghdl-plugin/build.sh from https://github.com/timonix/OneWare.PslExtension with MSYS2 mingw64 gcc $GCC_VERSION

ghdl.so: ghdl-yosys-plugin ($PLUGIN_REPO) commit $PLUGIN_COMMIT
         with default-prefix.patch from tools/ghdl-plugin
         License: GPL-3.0-or-later (LICENSE-ghdl-yosys-plugin.txt)
         Statically linked with libghdl and the GNAT runtime of MSYS2 mingw-w64-x86_64-gcc-ada.
libghdl: GHDL ($GHDL_REPO) commit $GHDL_COMMIT ($GHDL_DESCRIBE)
         with ghdl-ar-response-file.patch from tools/ghdl-plugin
         License: GPL-2.0-or-later (LICENSE-ghdl.md)
lib/:    GHDL std and ieee libraries from the same GHDL build.
         The IEEE library sources carry their own license headers.

Source: $SRC_NAME.tar.gz, published next to this zip (release ghdl-plugin-<OSS CAD Suite version> of the
repository above). It contains the complete source of GHDL and ghdl-yosys-plugin at the commits above,
the patches and the build script.
EOF

ZIP="$OUT/ghdl-plugin-win-x64-$OSS_VERSION.zip"
rm -f "$ZIP"
# By full path, the MSYS2 shell of CI runners does not have it on PATH
"$(cygpath -u "${SYSTEMROOT:-C:\\Windows}")/System32/WindowsPowerShell/v1.0/powershell.exe" -NoProfile -Command "Compress-Archive -Path '$(cygpath -w "$PKG")\\*' -DestinationPath '$(cygpath -w "$ZIP")'"
echo "Built $ZIP"

# Corresponding source for the GPL: both projects at the built commits (unpatched), the patches and this script
SRC_TAR="$OUT/$SRC_NAME.tar"
mkdir -p "$WORK/src-extra/$SRC_NAME/build"
cp "$SCRIPT_DIR/build.sh" "$SCRIPT_DIR/default-prefix.patch" "$SCRIPT_DIR/ghdl-ar-response-file.patch" "$WORK/src-extra/$SRC_NAME/build/"
cat > "$WORK/src-extra/$SRC_NAME/README.txt" <<EOF
Source of ghdl-plugin-win-x64-$OSS_VERSION.zip (GHDL plugin for the yosys of Windows OSS CAD Suite $OSS_VERSION).

ghdl/               GHDL ($GHDL_REPO) at commit $GHDL_COMMIT ($GHDL_DESCRIBE), GPL-2.0-or-later
ghdl-yosys-plugin/  ghdl-yosys-plugin ($PLUGIN_REPO) at commit $PLUGIN_COMMIT, GPL-3.0-or-later
build/              build.sh and the patches it applies: ghdl-ar-response-file.patch to ghdl/,
                    default-prefix.patch to ghdl-yosys-plugin/

Built in MSYS2 mingw64 with gcc $GCC_VERSION (mingw-w64-x86_64-gcc, mingw-w64-x86_64-gcc-ada) against the yosys
headers and yosys.exe of OSS CAD Suite $OSS_VERSION (https://github.com/YosysHQ/oss-cad-suite-build):
  build/build.sh <oss-cad-suite folder> $GHDL_COMMIT $PLUGIN_COMMIT
build.sh clones both projects at those commits; the sources here are the same trees.
EOF
git -C ghdl-src archive --format=tar --prefix="$SRC_NAME/ghdl/" -o "$SRC_TAR" HEAD
git -C plugin archive --format=tar --prefix="$SRC_NAME/ghdl-yosys-plugin/" -o "$WORK/plugin-src.tar" HEAD
tar -Af "$SRC_TAR" "$WORK/plugin-src.tar"
tar -rf "$SRC_TAR" -C "$WORK/src-extra" "$SRC_NAME"
rm -f "$SRC_TAR.gz" && gzip -9 "$SRC_TAR"
echo "Built $SRC_TAR.gz"
