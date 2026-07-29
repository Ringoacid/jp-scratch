# JP Scratch

WSL や一部アプリで日本語が打てないときに、**どこからでも 1 アクションで呼び出せてすぐ消える**常駐メモ帳。

仕様は [requirements.md](requirements.md) を参照。現在 **v1（常駐エディタ）まで実装済み**。
v2（Gemini による校正）と v3（文体の学習）は未着手。

---

## 使い方

| 操作 | 割り当て |
|---|---|
| 表示 / 非表示 | `Alt + Space`（グローバル） |
| 全文をコピーして隠す | `Ctrl + Alt + Enter`（グローバル。直前のウィンドウにフォーカスを戻す） |
| 新しいタブ / 閉じる / 復元 | `Ctrl+T` / `Ctrl+W` / `Ctrl+Shift+T` |
| タブ切替 | `Ctrl+Tab` / `Ctrl+Shift+Tab` |
| 検索 / 置換 | `Ctrl+F` / `Ctrl+H`、次・前は `F3` / `Shift+F3` |
| 全タブ横断検索 | `Ctrl+Shift+F` |
| `.txt` へエクスポート | `Ctrl+Shift+S` |
| 隠す | `Esc` |
| フォントサイズ | `Ctrl + マウスホイール` |

- タブ見出しは**ダブルクリックでリネーム**、**中クリックで閉じる**、**ドラッグで並べ替え**。
- 設定の「隠すときに本文をクリップボードへコピーする」を ON にすると、**どの隠し方でも**コピーされる
  （フォーカス喪失・`Alt+Space`・`Esc`・`✕`）。
- 「保存」操作はない。入力が止まると自動で書き込まれる。
- 終了はトレイアイコンの右クリック →「終了」。ウィンドウの `✕` は隠すだけ。

## データの置き場所

```
%APPDATA%\JpScratch\
├─ settings.json      設定
├─ app.db             タブのメタ情報（SQLite）
└─ tabs\
   ├─ {id}.txt        本文（UTF-8, BOM なし）
   └─ trash\{id}.txt  閉じたタブ（既定 30 日で自動削除）
```

本文は**プレーンテキスト**で保存する。このアプリが動かなくなってもメモ帳でサルベージできることを要件にしているため。
書き込みは一時ファイルに保存してから `File.Replace` で置き換えるため、書き込み中に落ちても既存の本文は壊れない。

---

## ビルド

前提: .NET 10 SDK

```powershell
dotnet build                 # デバッグビルド
dotnet run                   # 実行
```

### 煙テスト

```powershell
powershell -File tools\smoke-test.ps1 publish\fdd\JpScratch.exe
```

起動・日本語入力・自動保存・BOM なし UTF-8・非表示時のメモリ返却・二重起動時の呼び戻し・
再表示後のキャレット位置までを通しで確認する。UI 自動化は使わず `WM_CHAR` の送出だけで動く。

> **`%APPDATA%\JpScratch` を消してから走る**ので、実データがある状態では実行しないこと。

### 校正プロンプトの検証

v2へ組み込む前の独立検証アプリは `PromptValidation/` にある。

```powershell
$env:GEMINI_API_KEY = "..."
dotnet run --project PromptValidation
```

誤り例5本と文体保護例25本を一括評価する。個別実行、任意文章、ドライランなどの詳細は
[`PromptValidation/README.md`](PromptValidation/README.md) を参照。

### インストーラー (MSI)

前提: WiX v5（`dotnet tool install --global wix --version 5.0.2`）

```powershell
powershell -File installer\build.ps1                  # フレームワーク依存 (約 1.8 MB)
powershell -File installer\build.ps1 -SelfContained   # ランタイム同梱 (約 70 MB)
```

出力は `publish\msi\`。ユーザー単位インストール（`%LOCALAPPDATA%\Programs\JP Scratch`）なので管理者権限は不要。
スタートメニューのショートカットと `HKCU\...\Run` のスタートアップ登録を作る。

> WiX v6 以降は Open Source Maintenance Fee の EULA 同意が必要になる。
> 同意するかは利用者側の判断なので、ここでは v5 に固定している。

---

## 構成

```
App.xaml.cs              起動・常駐・単一インスタンス・クラッシュ時の保存
Assets/app.ico           アプリアイコン（小サイズは DIB。NotifyIcon が PNG を展開できないため）
Controls/                検索・置換パネル
Editor/                  AvalonEdit の拡張（検索ハイライト、全角スペース可視化、フォント解決）
Infrastructure/          Win32 相互運用、一時ファイル経由の安全なファイル書き込み、パス解決
Models/                  設定・タブ・ホットキー
Services/                設定・SQLite・タブ管理・ホットキー・ウィンドウ配置・テーマ・トレイ
Themes/                  ライト / ダーク / 共通スタイル
Views/                   メインウィンドウ、設定、全タブ検索
installer/               WiX による MSI
```

### 実装上、後から触るときに注意が要る箇所

- **ウィンドウ位置は物理ピクセルで計算している**（`Services/WindowPlacer.cs`）。
  WPF の `Window.Left/Top` は混在 DPI のマルチモニタで素直に扱えないため、`SetWindowPos` を直接使う。
  設定に持つのは「サイズ = DIP」「位置 = 物理ピクセル」。
- **WinForms の暗黙 using を切っている**（`jp-scratch.csproj`）。
  通知領域アイコンのためだけに WinForms を参照しており、有効なままだと `Brush` `Point` `KeyEventArgs` などが WPF 側と全面衝突する。
- **IME 変換中は自動非表示を止める**（`NativeMethods.HasImeComposition`）。
  変換中にウィンドウが消えると入力そのものが失われる。メッセージのフックではなく、必要な瞬間に `ImmGetCompositionString` で問い合わせている。
- **テーマ辞書は `ThemeService` だけが差し込む**。App.xaml で読み込むと二重にマージされて切り替えが効かなくなる。
- **二重起動時の呼び戻しは名前付きイベントで行う**（`Infrastructure/SingleInstance.cs`）。
  ウィンドウメッセージのブロードキャストは使えない。`ShowInTaskbar="False"` のせいで WPF が隠しオーナーウィンドウを作るため
  メインウィンドウが「所有されたウィンドウ」になり、`PostMessage(HWND_BROADCAST)` の配送対象から外れる。
- **PowerShell スクリプトは UTF-8 BOM 付きで保存すること**。
  BOM がないと Windows PowerShell 5.1 が日本語を CP932 として読み、コメントが次の行を巻き込んで壊す。
