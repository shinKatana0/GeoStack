#!/bin/sh
# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
set -eu
cd "$GEOSTACK_FFMPEG_BUILD/../zlib-1.3.1"
make -f win32/Makefile.gcc libz.a
cd "$GEOSTACK_FFMPEG_BUILD"
./configure --target-os=mingw32 --arch=x86_64 --cc=gcc --cxx=g++ --disable-autodetect --disable-network --disable-x86asm --disable-debug --disable-doc --disable-ffplay --disable-ffprobe --disable-shared --enable-static --disable-gpl --disable-nonfree --disable-version3 --disable-iconv --disable-bzlib --disable-lzma --enable-zlib --extra-cflags='-O2 -I../zlib-1.3.1' --extra-ldflags='-static -L../zlib-1.3.1'
make -j8 V=1 ffmpeg.exe
