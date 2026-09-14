# ScreenshotApp

Windows タスクトレイに常駐し、`Ctrl+Shift+S` で画面上の矩形範囲を選択してスクリーンショットを撮る常駐アプリです。

## 機能

- タスクトレイ常駐（NotifyIcon）
- グローバルホットキー `Ctrl+Shift+S`（アプリがアクティブでなくても動作）
- ドラッグで矩形範囲を選択 → キャプチャ（マルチモニター対応）
- `Ctrl+1` で、範囲選択をやり直さず直前と同じ範囲を再キャプチャ
- 選択オーバーレイの灰色がキャプチャ画像に写り込まないよう、非表示後にキャプチャする対策済み
- キャプチャ画像は縦横比を維持したまま **60%** にリサイズしてから保存・コピー（倍率は `TrayApplicationContext.cs` の `ResizeScale` で変更可）
- 撮影結果はクリップボードに自動コピー
- 既定では `%USERPROFILE%\Pictures\Screenshots` に PNG として保存（トレイメニューから「クリップボードにのみコピー」に切り替え可）
- Windows 起動時の自動起動 ON/OFF（トレイメニューのチェックボックス）
- Esc で選択キャンセル
- 二重起動防止

## 必要環境

- Windows 10 / 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)（ビルド時のみ。実行だけなら自己完結ビルドで SDK 不要にできます）

## ビルド方法

プロジェクトフォルダで以下を実行します。

```powershell
# 通常ビルド（要 .NET 8 ランタイム）
dotnet build -c Release

# 実行ファイル1つにまとめて配布したい場合（自己完結・単一exe）
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

`dotnet publish` の場合、生成物は
`bin\Release\net8.0-windows\win-x64\publish\ScreenshotApp.exe`
に出力されます。この exe だけコピーすれば他の Windows PC でも .NET ランタイムなしで動作します。

## 実行方法

1. `ScreenshotApp.exe` を起動する（コンソールは開きません。タスクトレイにアイコンが表示されます）
2. `Ctrl+Shift+S` を押す、またはトレイアイコンをダブルクリック
3. 画面が少し暗くなるので、キャプチャしたい範囲をドラッグして選択
4. マウスを離すとキャプチャされ、60%にリサイズした上でクリップボードにコピー（設定により保存も）されます
5. 同じ範囲をもう一度撮りたいときは `Ctrl+1`（選択操作は不要）
6. トレイアイコン右クリックで各種設定・終了ができます

## カスタマイズしたい場合

- **ホットキーを変更したい**：`TrayApplicationContext.cs` 内の
  ```csharp
  _captureHotkeyId = _hotkeyManager.Register(HotkeyManager.Modifiers.Control | HotkeyManager.Modifiers.Shift, Keys.S);
  _repeatHotkeyId  = _hotkeyManager.Register(HotkeyManager.Modifiers.Control, Keys.D1);
  ```
  の修飾キー・キーを変更してください。
- **リサイズ倍率を変更したい**：`TrayApplicationContext.cs` 先頭付近の
  ```csharp
  private const double ResizeScale = 0.6;
  ```
  を変更してください（例: `1.0` にすればリサイズなし）。
- **保存先フォルダを変更したい**：`ScreenCapture.cs` の `GetSaveDirectory()` を編集してください。
- **アイコンを差し替えたい**：`app.ico` をプロジェクトに追加し、`ScreenshotApp.csproj` の
  `<ApplicationIcon>app.ico</ApplicationIcon>` のコメントを外してください（現在はコードでアイコンを動的生成しています）。

## ファイル構成

```
ScreenshotApp/
├── ScreenshotApp.csproj      プロジェクトファイル (.NET 8 / WinForms)
├── Program.cs                 エントリポイント（二重起動防止つき）
├── TrayApplicationContext.cs  トレイ常駐・メニュー・ホットキー制御のメイン処理
├── HotkeyManager.cs           RegisterHotKey の Win32 ラッパー
├── OverlayForm.cs             矩形選択用の全画面オーバーレイ
├── ScreenCapture.cs           画面キャプチャ・リサイズ・保存・クリップボードコピー
└── README.md
```
