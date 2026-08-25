Place bundled native tools here.

- `libimagequant.dll` (or `imagequant.dll`) — PNG quantization
- `jpegtran.exe` (or `mozjpeg.exe`) — JPEG lossless re-encoding
- `oxipng.exe` — PNG lossless optimization

All three are optional. Without them the application falls back to ImageSharp's
own encoders; the "压缩引擎" panel in the window reports which one is actually in use.
