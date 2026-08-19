@echo off
REM Whispy for Windows — build a self-contained single-file exe.
REM Requires the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
cd /d "%~dp0"

echo Building Whispy (release, self-contained)...
dotnet publish Whispy.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o dist

if %errorlevel% neq 0 (
  echo.
  echo Build FAILED. Copy the errors above and send them back.
  exit /b 1
)

echo.
echo Done. Your app is: dist\Whispy.exe
echo Share that single file with friends — no .NET install needed on their PCs.
echo First launch downloads the Whisper model (about 500 MB, one time).
