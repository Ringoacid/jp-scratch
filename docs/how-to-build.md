# ビルド方法・インストーラー作成方法

ビルドには`.NET SDK 10.0.400`を導入し、インストーラーの作成には`WiX Toolset 5.0.2`を導入してください。

Windowsで、プロジェクト直下から実行してください。

## ビルドして起動する

```powershell
dotnet build .\jp-scratch.csproj
dotnet run --project .\jp-scratch.csproj
```

初回のビルドではNuGetからライブラリをダウンロードします。

開発中に普段のメモや設定を使いたくない場合は、起動前にデータの保存先を変えることができます。指定したフォルダーにテスト用のデータが残ります。

```powershell
$env:JPSCRATCH_DATA_DIR = Join-Path $env:TEMP 'JpScratch-dev'
dotnet run --project .\jp-scratch.csproj
```

この指定は、そのPowerShellから起動したアプリに使われます。元に戻す場合はアプリを終了してから、新しいPowerShellを開くか、`Remove-Item Env:JPSCRATCH_DATA_DIR` で指定を外してください。

## テストする

```powershell
dotnet run --project .\PromptValidation\PromptValidation.csproj -- --self-test
```

外部APIを呼ばないテストです。APIキーは必要ありません。校正の差分処理、保存、料金計算、サブスク接続などを確認します。

`PromptValidation` を引数なしで実行すると、実際のAPIを使います。料金が発生するため、オフラインで確認するときは `--self-test` を付けてください。ほかの実行方法は [PromptValidationのREADME](../PromptValidation/README.md)に記載しています。

## インストーラーを作る

プロジェクト直下で以下のスクリプトを実行すると、リリースビルドとインストーラーの作成を行います。

```powershell
# フレームワーク依存（.NET 10 Desktop Runtimeが必要）
powershell -File installer\build.ps1

# ランタイム同梱
powershell -File installer\build.ps1 -SelfContained

# 署名つき
powershell -File installer\build.ps1 -Sign -CertificateThumbprint '<thumb>'
```

MSIは `publish\msi` に出力されます。アプリ本体は、フレームワーク依存版なら `publish\fdd`、ランタイム同梱版なら `publish\scd` に出力されます。どちらもx64向けです。

署名する場合は、現在のWindowsユーザーの証明書ストアに秘密鍵付きの証明書を用意し、Windows SDKの `signtool.exe` をPATHから実行できるようにしてください。`<thumb>` には証明書の拇印を指定します。

新しい機能を追加したり既存の機能を修正したりしてインストーラーを作成する場合は、バージョン番号を上げることも検討してください。
[jp-scratch.csproj](../jp-scratch.csproj) の`<Version>`タグを変更することで、バージョン番号を上げることができます。

ビルドしたアプリの起動・保存・常駐は、以下のスクリプトでも確認できます。

```powershell
powershell -File tools\smoke-test.ps1 publish\fdd\JpScratch.exe
```

このテストは一時フォルダーにデータを作り、終了時に削除します。JP Scratchがすでに起動している場合はテストを中止するので、先にタスクトレイから終了してください。
