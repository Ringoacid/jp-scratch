# モデル仕様書: Gemini 3.5 Flash-Lite

## 1. 概要 (Overview)
- **モデル名**: Gemini 3.5 Flash-Lite
- **モデルID (Code)**: `gemini-3.5-flash-lite`
- **安定版ID**: `gemini-3.5-flash-lite`
- **概要**: 低レイテンシかつ費用対効果の高いマルチモーダルモデル。サブエージェントタスクやドキュメント解析の高速・低コスト実行向けに最適化されている。
- **主なユースケース**: 大容量のエージェントワークフロー、シンプルなデータ抽出、レイテンシやAPI費用が制約となるアプリケーション。
- **最終更新日**: 2026年7月23日 (UTC)

## 2. トークン上限 (Token Limits)
- **入力トークン上限**: 1,048,576 トークン (~1M)
- **出力トークン上限**: 65,536 トークン (~64K)

## 3. 入出力仕様 (I/O Capabilities)
- **入力フォーマット**: テキスト、画像、動画、音声、PDF
- **出力フォーマット**: テキスト

## 4. サポート機能一覧 (Features)
| 機能 | サポート状況 |
| :--- | :--- |
| 思考 (Thinking) | サポート対象 |
| 関数呼び出し (Function Calling) | サポート対象 |
| 構造化出力 (Structured Outputs) | サポート対象 |
| コード実行 (Code Execution) | サポート対象 |
| コンテキストのキャッシュ保存 (Caching) | サポート対象 |
| ファイル検索 (File Search) | サポート対象 |
| URL コンテキスト (URL Context) | サポート対象 |
| 検索によるグラウンディング (Search Grounding) | サポート対象 |
| Google マップによるグラウンディング (Maps Grounding) | サポート対象 |
| パソコンの使用 (Computer Use) | サポート対象 (プレビュー) |
| 音声生成 (Audio Generation) | サポート対象外 |
| 画像生成 (Image Generation) | サポート対象外 |
| Live API | サポート対象外 |

## 5. 使用・推論オプション (Serving Options)
- **Batch API**: サポート対象
- **Flex 推論 (Flex Inference)**: サポート対象
- **優先度推論 (Priority Inference)**: サポート対象