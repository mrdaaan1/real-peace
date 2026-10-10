#!/bin/bash
# Собрать Real Peace для Mac (универсальная сборка: Apple Silicon + Intel) и упаковать в zip для передачи.
# Перед запуском закройте Unity с этим проектом. Запуск:  bash Tools/build_mac.sh
set -e
PROJECT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION=$(grep 'm_EditorVersion:' "$PROJECT/ProjectSettings/ProjectVersion.txt" | awk '{print $2}')
UNITY="/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/MacOS/Unity"
if [ ! -x "$UNITY" ]; then echo "Не нашёл Unity $VERSION в /Applications/Unity/Hub/Editor"; exit 1; fi
if pgrep -fl "Unity.app/Contents/MacOS/Unity" | grep -iF -- "$PROJECT" >/dev/null 2>&1; then
  echo "Проект открыт в Unity — закройте Unity (Cmd+Q) и запустите скрипт снова."; exit 1
fi
if [ -f "$PROJECT/Temp/UnityLockfile" ] && ! pgrep -x Unity >/dev/null 2>&1; then rm -f "$PROJECT/Temp/UnityLockfile"; fi

APP="$PROJECT/Builds/Mac/Real Peace.app"
ZIP="$PROJECT/Builds/RealPeace-mac.zip"
mkdir -p "$PROJECT/Builds"
rm -rf "$APP" "$ZIP"
echo "Собираю игру (5–10 минут, окно Unity не откроется)..."
if ! "$UNITY" -batchmode -projectPath "$PROJECT" -executeMethod GameBuilder.BuildMacBatch -logFile "$PROJECT/Builds/build_mac.log"; then
  echo "Сборка не удалась. Последние ошибки из $PROJECT/Builds/build_mac.log:"
  grep -iE "error|exception" "$PROJECT/Builds/build_mac.log" | tail -20
  exit 1
fi
echo "Подписываю (ad-hoc) и упаковываю..."
xattr -cr "$APP"
codesign --force --deep --sign - "$APP"
ditto -c -k --sequesterRsrc --keepParent "$APP" "$ZIP"
echo
echo "Готово!"
echo "  Игра:   $APP"
echo "  Архив:  $ZIP ($(du -h "$ZIP" | cut -f1))"
