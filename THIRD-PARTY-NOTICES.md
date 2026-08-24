# サードパーティー通知（THIRD-PARTY NOTICES）

JP Scratch は以下のサードパーティーコンポーネントを利用し、その一部を頒布物（`publish\fdd` および MSI インストーラー）に同梱しています。各コンポーネントの著作権は原著作者に帰属し、以下に示す条件のもとで利用しています。

ライセンス本文は原文（英語）のまま掲載しています。日本語の説明文は参考であり、法的効力を持つのは原文です。

頒布物に同梱される実体は次のファイルです。

| ファイル | 由来するパッケージ |
| --- | --- |
| `ICSharpCode.AvalonEdit.dll` | AvalonEdit |
| `Microsoft.Data.Sqlite.dll` | Microsoft.Data.Sqlite.Core |
| `SQLitePCLRaw.batteries_v2.dll` | SQLitePCLRaw.bundle_e_sqlite3 |
| `SQLitePCLRaw.core.dll` | SQLitePCLRaw.core |
| `SQLitePCLRaw.provider.e_sqlite3.dll` | SQLitePCLRaw.provider.e_sqlite3 |
| `e_sqlite3.dll` | SQLitePCLRaw.lib.e_sqlite3（中身は SQLite 本体） |

---

## 1. AvalonEdit 6.3.1.120

- 用途: 本文編集に使うテキストエディターコントロール
- 発行者: AvalonEdit Contributors
- プロジェクト: <http://www.avalonedit.net/> / <https://github.com/icsharpcode/AvalonEdit>
- ライセンス: MIT

```
Copyright (c) 2000-2025 AlphaSierraPapa for the SharpDevelop Team

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

---

## 2. Microsoft.Data.Sqlite 10.0.10 / Microsoft.Data.Sqlite.Core 10.0.10

- 用途: タブ・課金ログ・スタイルガイドを保存する SQLite の ADO.NET プロバイダー
- 発行者: Microsoft
- プロジェクト: <https://docs.microsoft.com/dotnet/standard/data/sqlite/> / <https://github.com/dotnet/dotnet>
- ライセンス: MIT

`Microsoft.Data.Sqlite` はメタパッケージで、頒布物に入る `Microsoft.Data.Sqlite.dll` は `Microsoft.Data.Sqlite.Core` に由来します。

```
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## 3. SQLitePCLRaw 2.1.12

対象パッケージ: `SQLitePCLRaw.bundle_e_sqlite3` / `SQLitePCLRaw.core` / `SQLitePCLRaw.lib.e_sqlite3` / `SQLitePCLRaw.provider.e_sqlite3`

- 用途: Microsoft.Data.Sqlite が SQLite 本体を呼び出すためのネイティブ相互運用層
- 発行者: Eric Sink（SourceGear, LLC）
- プロジェクト: <https://github.com/ericsink/SQLitePCL.raw>
- ライセンス: Apache License 2.0
- 著作権表示: `Copyright 2014-2024 SourceGear, LLC`

なお `SQLitePCLRaw.lib.e_sqlite3` に含まれるネイティブライブラリ `e_sqlite3.dll` の中身は SQLite 本体であり、そちらはパブリックドメインです（第 4 節を参照）。Apache License 2.0 が適用されるのは SQLitePCLRaw 側のコードです。

Apache License 2.0 の全文:

```

                                 Apache License
                           Version 2.0, January 2004
                        http://www.apache.org/licenses/

   TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION

   1. Definitions.

      "License" shall mean the terms and conditions for use, reproduction,
      and distribution as defined by Sections 1 through 9 of this document.

      "Licensor" shall mean the copyright owner or entity authorized by
      the copyright owner that is granting the License.

      "Legal Entity" shall mean the union of the acting entity and all
      other entities that control, are controlled by, or are under common
      control with that entity. For the purposes of this definition,
      "control" means (i) the power, direct or indirect, to cause the
      direction or management of such entity, whether by contract or
      otherwise, or (ii) ownership of fifty percent (50%) or more of the
      outstanding shares, or (iii) beneficial ownership of such entity.

      "You" (or "Your") shall mean an individual or Legal Entity
      exercising permissions granted by this License.

      "Source" form shall mean the preferred form for making modifications,
      including but not limited to software source code, documentation
      source, and configuration files.

      "Object" form shall mean any form resulting from mechanical
      transformation or translation of a Source form, including but
      not limited to compiled object code, generated documentation,
      and conversions to other media types.

      "Work" shall mean the work of authorship, whether in Source or
      Object form, made available under the License, as indicated by a
      copyright notice that is included in or attached to the work
      (an example is provided in the Appendix below).

      "Derivative Works" shall mean any work, whether in Source or Object
      form, that is based on (or derived from) the Work and for which the
      editorial revisions, annotations, elaborations, or other modifications
      represent, as a whole, an original work of authorship. For the purposes
      of this License, Derivative Works shall not include works that remain
      separable from, or merely link (or bind by name) to the interfaces of,
      the Work and Derivative Works thereof.

      "Contribution" shall mean any work of authorship, including
      the original version of the Work and any modifications or additions
      to that Work or Derivative Works thereof, that is intentionally
      submitted to Licensor for inclusion in the Work by the copyright owner
      or by an individual or Legal Entity authorized to submit on behalf of
      the copyright owner. For the purposes of this definition, "submitted"
      means any form of electronic, verbal, or written communication sent
      to the Licensor or its representatives, including but not limited to
      communication on electronic mailing lists, source code control systems,
      and issue tracking systems that are managed by, or on behalf of, the
      Licensor for the purpose of discussing and improving the Work, but
      excluding communication that is conspicuously marked or otherwise
      designated in writing by the copyright owner as "Not a Contribution."

      "Contributor" shall mean Licensor and any individual or Legal Entity
      on behalf of whom a Contribution has been received by Licensor and
      subsequently incorporated within the Work.

   2. Grant of Copyright License. Subject to the terms and conditions of
      this License, each Contributor hereby grants to You a perpetual,
      worldwide, non-exclusive, no-charge, royalty-free, irrevocable
      copyright license to reproduce, prepare Derivative Works of,
      publicly display, publicly perform, sublicense, and distribute the
      Work and such Derivative Works in Source or Object form.

   3. Grant of Patent License. Subject to the terms and conditions of
      this License, each Contributor hereby grants to You a perpetual,
      worldwide, non-exclusive, no-charge, royalty-free, irrevocable
      (except as stated in this section) patent license to make, have made,
      use, offer to sell, sell, import, and otherwise transfer the Work,
      where such license applies only to those patent claims licensable
      by such Contributor that are necessarily infringed by their
      Contribution(s) alone or by combination of their Contribution(s)
      with the Work to which such Contribution(s) was submitted. If You
      institute patent litigation against any entity (including a
      cross-claim or counterclaim in a lawsuit) alleging that the Work
      or a Contribution incorporated within the Work constitutes direct
      or contributory patent infringement, then any patent licenses
      granted to You under this License for that Work shall terminate
      as of the date such litigation is filed.

   4. Redistribution. You may reproduce and distribute copies of the
      Work or Derivative Works thereof in any medium, with or without
      modifications, and in Source or Object form, provided that You
      meet the following conditions:

      (a) You must give any other recipients of the Work or
          Derivative Works a copy of this License; and

      (b) You must cause any modified files to carry prominent notices
          stating that You changed the files; and

      (c) You must retain, in the Source form of any Derivative Works
          that You distribute, all copyright, patent, trademark, and
          attribution notices from the Source form of the Work,
          excluding those notices that do not pertain to any part of
          the Derivative Works; and

      (d) If the Work includes a "NOTICE" text file as part of its
          distribution, then any Derivative Works that You distribute must
          include a readable copy of the attribution notices contained
          within such NOTICE file, excluding those notices that do not
          pertain to any part of the Derivative Works, in at least one
          of the following places: within a NOTICE text file distributed
          as part of the Derivative Works; within the Source form or
          documentation, if provided along with the Derivative Works; or,
          within a display generated by the Derivative Works, if and
          wherever such third-party notices normally appear. The contents
          of the NOTICE file are for informational purposes only and
          do not modify the License. You may add Your own attribution
          notices within Derivative Works that You distribute, alongside
          or as an addendum to the NOTICE text from the Work, provided
          that such additional attribution notices cannot be construed
          as modifying the License.

      You may add Your own copyright statement to Your modifications and
      may provide additional or different license terms and conditions
      for use, reproduction, or distribution of Your modifications, or
      for any such Derivative Works as a whole, provided Your use,
      reproduction, and distribution of the Work otherwise complies with
      the conditions stated in this License.

   5. Submission of Contributions. Unless You explicitly state otherwise,
      any Contribution intentionally submitted for inclusion in the Work
      by You to the Licensor shall be under the terms and conditions of
      this License, without any additional terms or conditions.
      Notwithstanding the above, nothing herein shall supersede or modify
      the terms of any separate license agreement you may have executed
      with Licensor regarding such Contributions.

   6. Trademarks. This License does not grant permission to use the trade
      names, trademarks, service marks, or product names of the Licensor,
      except as required for reasonable and customary use in describing the
      origin of the Work and reproducing the content of the NOTICE file.

   7. Disclaimer of Warranty. Unless required by applicable law or
      agreed to in writing, Licensor provides the Work (and each
      Contributor provides its Contributions) on an "AS IS" BASIS,
      WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
      implied, including, without limitation, any warranties or conditions
      of TITLE, NON-INFRINGEMENT, MERCHANTABILITY, or FITNESS FOR A
      PARTICULAR PURPOSE. You are solely responsible for determining the
      appropriateness of using or redistributing the Work and assume any
      risks associated with Your exercise of permissions under this License.

   8. Limitation of Liability. In no event and under no legal theory,
      whether in tort (including negligence), contract, or otherwise,
      unless required by applicable law (such as deliberate and grossly
      negligent acts) or agreed to in writing, shall any Contributor be
      liable to You for damages, including any direct, indirect, special,
      incidental, or consequential damages of any character arising as a
      result of this License or out of the use or inability to use the
      Work (including but not limited to damages for loss of goodwill,
      work stoppage, computer failure or malfunction, or any and all
      other commercial damages or losses), even if such Contributor
      has been advised of the possibility of such damages.

   9. Accepting Warranty or Additional Liability. While redistributing
      the Work or Derivative Works thereof, You may choose to offer,
      and charge a fee for, acceptance of support, warranty, indemnity,
      or other liability obligations and/or rights consistent with this
      License. However, in accepting such obligations, You may act only
      on Your own behalf and on Your sole responsibility, not on behalf
      of any other Contributor, and only if You agree to indemnify,
      defend, and hold each Contributor harmless for any liability
      incurred by, or claims asserted against, such Contributor by reason
      of your accepting any such warranty or additional liability.

   END OF TERMS AND CONDITIONS

   APPENDIX: How to apply the Apache License to your work.

      To apply the Apache License to your work, attach the following
      boilerplate notice, with the fields enclosed by brackets "[]"
      replaced with your own identifying information. (Don't include
      the brackets!)  The text should be enclosed in the appropriate
      comment syntax for the file format. We also recommend that a
      file or class name and description of purpose be included on the
      same "printed page" as the copyright notice for easier
      identification within third-party archives.

   Copyright [yyyy] [name of copyright owner]

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
```

---

## 4. SQLite

- 用途: 設定・タブ・課金ログの保存に使う組み込みデータベース本体（`e_sqlite3.dll`）
- 発行者: D. Richard Hipp ほか SQLite の作者陣
- プロジェクト: <https://www.sqlite.org/>
- ライセンス: パブリックドメイン（著作権を放棄）

SQLite のソースコードはパブリックドメインに置かれています。作者による原文の宣言（"blessing"）は次のとおりです。

```
2001 September 15

The author disclaims copyright to this source code.  In place of
a legal notice, here is a blessing:

  *   May you do good and not evil.
  *   May you find forgiveness for yourself and forgive others.
  *   May you share freely, never taking more than you give.
```

---

## 5. .NET / .NET Desktop Runtime

- 発行者: Microsoft / .NET Foundation
- プロジェクト: <https://github.com/dotnet/runtime> / <https://github.com/dotnet/wpf>
- ライセンス: MIT（同梱される第三者コンポーネントについては .NET 自身の THIRD-PARTY-NOTICES を参照）

既定のフレームワーク依存ビルド（`publish\fdd`、`JpScratch-<version>.msi`）は .NET ランタイムを同梱せず、利用者の環境にインストール済みの .NET 10 Desktop Runtime を使います。この場合、本アプリケーションはランタイムを再頒布していません。

`installer\build.ps1 -SelfContained` で作る自己完結ビルド（`publish\scd`、`JpScratch-<version>-selfcontained.msi`）は .NET 10 Desktop Runtime を同梱するため、ランタイムを再頒布することになります。その場合に適用される通知は、ランタイム配布物に含まれる `THIRD-PARTY-NOTICES.TXT`、および <https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT> を参照してください（本ファイルでは複製しません）。

---

## 6. ビルド時のみ使用するもの（頒布物には含まれない）

以下は開発・ビルド・検証にのみ使うツールで、生成される実行ファイルや MSI には一切含まれません。参考として記載します。

| ツール | 用途 | ライセンス |
| --- | --- | --- |
| WiX Toolset v5 | MSI の生成（`installer\build.ps1`） | MS-RL |
| Python 3 | `tools\*.py` の実行 | PSF License |
| Pillow | トレイアイコン生成（`tools\build-tray-icons.py`） | MIT-CMU |
| Matplotlib | ベンチマーク比較図の生成（`tools\plot-model-benchmark.py`） | Matplotlib License（BSD 系） |
| Graphify 0.9.48 | コードベースの知識グラフとエージェント用スキル | Apache License 2.0（旧MIT部分を含む） |

WiX でビルドした MSI に WiX 自身のコードは埋め込まれないため、MS-RL の頒布条件は成果物には及びません。

`PromptValidation`（オフライン回帰テスト・モデル比較用のコンソールアプリ）は本体と同じ NuGet パッケージ（AvalonEdit / Microsoft.Data.Sqlite / SQLitePCLRaw）を参照しますが、これも頒布物には含まれません。

Graphify のプロジェクト用スキルは `.claude\skills\graphify\` に含まれます。著作権表示と再配布条件は、同ディレクトリの `NOTICE`、`LICENSE`、`LICENSE-MIT` を参照してください。

---

## 7. 外部 API サービスについて

本アプリケーションは校正機能で Google（Gemini）・OpenAI・Anthropic・Preferred Networks（PLaMo）の各 API を HTTP で呼び出しますが、これらのベンダーが提供するクライアント SDK やライブラリは一切利用しておらず、`System.Net.Http` による自前実装です。したがって、これらのサービスに関してソフトウェアライセンス上の同梱物はありません。API の利用条件は各ベンダーの利用規約に従います。

---

## 更新方法

依存関係を変更したら、次のコマンドで実際の依存を確認したうえで本ファイルを更新してください。

```powershell
dotnet list package --include-transitive
```
