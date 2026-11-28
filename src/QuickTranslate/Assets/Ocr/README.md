# 内置英文 OCR

- `QuickTranslate.Tesseract.dll` 由 [Tesseract 5.5.2](https://github.com/tesseract-ocr/tesseract/tree/5.5.2) 和 [Leptonica 1.87.0](https://github.com/DanBloomberg/leptonica/tree/1.87.0) 构建。C++ 运行时静态链接，DLL 的导入表仅包含 Windows 系统库和 Windows 自带的 UCRT。
- `eng.traineddata` 来自官方 [tessdata_fast 英文模型](https://github.com/tesseract-ocr/tessdata_fast/blob/923915d4ced2a7235221788285785a29c4a42d4a/eng.traineddata)。SHA-256 为 `7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2`。
- [第三方许可](THIRD-PARTY-NOTICES.txt) 随安装包分发。

## 重建 Windows x64 引擎

准备 [LLVM-MinGW 20261117 UCRT x86_64](https://github.com/mstorsjo/llvm-mingw/releases/tag/20261117)、CMake 4.2.3 和 Ninja 1.13.2，将它们的可执行文件加入 PATH。将上述两个版本的上游源码解压到构建目录的 `leptonica` 和 `tesseract` 子目录，再从该目录运行以下 PowerShell 命令。将最后生成的 DLL 复制到本目录。

```powershell
$common = @(
    '-G', 'Ninja', '-DCMAKE_BUILD_TYPE=Release', '-DSW_BUILD=OFF',
    '-DCMAKE_C_COMPILER=x86_64-w64-mingw32-clang',
    '-DCMAKE_CXX_COMPILER=x86_64-w64-mingw32-clang++'
)
$leptonica = Join-Path $PWD 'lept-install'
cmake -S leptonica -B lept-build @common "-DCMAKE_INSTALL_PREFIX=$leptonica" `
    -DBUILD_SHARED_LIBS=OFF -DBUILD_PROG=OFF '-DCMAKE_C_FLAGS=-DMINIMUM_SEVERITY=L_SEVERITY_ERROR' `
    -DENABLE_ZLIB=OFF -DENABLE_PNG=OFF -DENABLE_GIF=OFF -DENABLE_JPEG=OFF `
    -DENABLE_TIFF=OFF -DENABLE_WEBP=OFF -DENABLE_OPENJPEG=OFF
cmake --build lept-build --target install

cmake -S tesseract -B tess-build @common "-DCMAKE_PREFIX_PATH=$leptonica" `
    -DBUILD_SHARED_LIBS=ON '-DCMAKE_SHARED_LINKER_FLAGS=-static-libgcc -static-libstdc++' `
    '-DCMAKE_CXX_FLAGS=-D_USE_MATH_DEFINES -DTESSERACT_DISABLE_DEBUG_FONTS' `
    -DENABLE_NATIVE=OFF -DOPENMP_BUILD=OFF -DGRAPHICS_DISABLED=ON -DDISABLED_LEGACY_ENGINE=OFF `
    -DDISABLE_ARCHIVE=ON -DDISABLE_CURL=ON -DDISABLE_TIFF=ON -DBUILD_TRAINING_TOOLS=OFF -DBUILD_TESTS=OFF
cmake --build tess-build --target libtesseract
llvm-strip --strip-debug -o QuickTranslate.Tesseract.dll tess-build/bin/libtesseract55.dll
llvm-readobj --coff-imports QuickTranslate.Tesseract.dll
```

更新后检查 DLL 导入表，并在禁用系统英文 OCR 的测试路径中验证内置模型能读取截图。
