# KonoAsset／BOOTH Library Manager とアイテム台帳の連携調査

調査日: 2026-10-22。対象: `HANDOFF.md` 4.6 の商品情報・サムネイル連携。調査のみで、実装・アプリのインストール・利用者の実データによる動作確認は行っていない。

## 結論

KonoAsset は公式公開ソースに JSON のモデルとローダーがあり、読み取り連携の検証を始めやすい。BLM も非公式 Unity 拡張で DB の読み取りが実装されており、連携の技術的な見通しはある。ただし BLM の内部 DB は公式公開 API としての保証を確認できず、利用許諾・対応バージョンの確認が残る。[K1](https://booth.pm/en/items/6641548)[K2](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/model/src/base.rs)[B1](https://booth.pm/announcements/893)[B2](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/README.md)

まず **KonoAsset の任意・読み取り専用の連携で、手動対応付けとサムネイル表示を検証する**。BLM は公開済みの BLMImporter Core を評価し、DB 読み取りの自前実装と比較する。商品バージョンと Unity への導入済み判定は、名前や更新日時から断定しない。以下の「連携案」は設計提案であり、実証済みの動作を意味しない。

## 調査範囲と根拠の扱い

- KonoAsset は配布ページと公式リポジトリを確認。配布ページの表示は v1.2.6、確認した `develop` の設定は `1.2.7-rc.0`。ソースの参照先はコミット `f83271e53f276df4bb0b60865cfbf86b42f1bbc4` に固定した。**このソースと配布済み v1.2.6 の完全一致は未確認**。[K1](https://booth.pm/en/items/6641548)[K3](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/tauri/src/lib.rs)
- BLM 本体の公式根拠は BOOTH のアーリーアクセス告知。保存形式の根拠は BLM 本体のソースではなく、**外部拡張の作者自身が公開している読み取り実装**。BLMImporter は確認時の `main` コミット `c8b6533002405378dce5b756c257bf95b547f66b`、Core の `package.json` は `1.1.0`。BLM 本体の最新バージョンと、この拡張が対応を保証する本体バージョンの範囲は未確認。[B1](https://booth.pm/announcements/893)[B2](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/README.md)[B3](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/package.json)
- 公開情報を検索・閲覧し、GitHub のファイルは接続ツールでも取得した。BLM の Discord 内の配布物・規約・更新履歴は確認していない。確認できない項目は以下で「未確認」とした。

## 1. KonoAsset

### 保存場所・形式

| 対象 | 確認できた内容 | 根拠 |
|---|---|---|
| データのルート | 設定モデルの `dataDirPath`。初期値は OS の Documents ディレクトリ配下の `KonoAsset`。Documents の取得失敗時はアプリのローカルデータ領域配下へフォールバックする。保存先を固定の `C:` パスとみなさない | [K3](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/tauri/src/lib.rs)[K4](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/model/src/preference.rs) |
| 設定ファイル | Tauri の `app_local_data_dir()` 配下の `preference.json`。設定は `version` と `data` を持つ JSON。確認ソースの設定形式番号は 6、保存先は `data.dataDirPath` | [K3](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/tauri/src/lib.rs)[K4](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/model/src/preference.rs)[K5](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/loader/src/preferences.rs) |
| 商品メタデータ | `<データルート>/metadata/avatars.json`、`avatarWearables.json`、`worldObjects.json`、`otherAssets.json`。SQLite ではなく JSON として読み書きする | [K6](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/storage/src/json_asset_container.rs)[K7](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/model/src/asset_trait.rs) |
| JSON の外形 | 確認ソースの各商品ファイルは `{"version":3,"data":[…]}`。`data` は商品集合。商品ファイルの形式番号とアプリの製品バージョンは別 | [K8](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/loader/src/avatar.rs)[K9](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/loader/src/avatar_wearables.rs)[K10](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/loader/src/world_objects.rs)[K11](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/loader/src/other_assets.rs) |
| サムネイル | `description.imageFilename` がファイル名。画像の基準ディレクトリは `<データルート>/images/`。画像未指定を許す | [K2](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/model/src/base.rs)[K6](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/storage/src/json_asset_container.rs) |
| 管理する実ファイル | ストレージ処理で `<データルート>/data/` を扱うことを確認。unitypackage はディレクトリを再帰走査して発見する。商品 UUID から実ファイルのサブディレクトリを作る厳密な規則は本調査では未確認 | [K12](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/storage/src/asset_storage.rs)[K13](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/storage/src/find.rs) |
| Windows／Mac の絶対パス | `preference.json` の OS 別の実配置は実機で未確認。取得元の Tauri API とアプリ識別子 `dev.konoasset.app` は確認。公式配布設定は Windows 向け NSIS であり、Mac 版の公式配布・動作保証は未確認 | [K3](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/tauri/src/lib.rs)[K14](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/tauri/tauri.conf.json) |

連携時はユーザーがアプリ設定で確認したデータルートを選べるようにする。`Documents/KonoAsset` は初期値の説明であり、全利用者に当てはまる固定パスではない。

### 商品スキーマの主な項目

JSON のキーは `camelCase`。次は公式 `model/src/base.rs` で確認したモデルである。[K2](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/model/src/base.rs)

| 欲しい情報 | 保存項目・意味 | 注意点 |
|---|---|---|
| 管理アセット ID | `id`（UUID） | BOOTH 商品 ID と別。BOOTH 以外の商品の対応付けにも使える |
| 商品名 | `description.name` | unitypackage 名と同じという保証はない |
| ショップ／作者 | `description.creator` | ショップサブドメイン等の独立フィールドは確認モデルにはない |
| BOOTH 商品 ID | `description.boothItemId`（任意の整数） | 未設定もある。文字列に正規化して外部キーとして持つ案が適する |
| サムネイル | `description.imageFilename`（任意） | `images/` 基準。実ファイルの有無も確認する |
| 対応アバター | `supportedAvatars`（衣装等の `AvatarWearable`） | 文字列集合。アバターの BOOTH ID 一覧ではない |
| カテゴリ | `category`（`AvatarWearable`／`WorldObject`／`OtherAsset`） | 台帳の `Outfit`／`Accessory` 等とは別体系 |
| タグ／メモ／依存先 | `description.tags`、`memo`、`dependencies` | 依存先は管理アセット UUID の一覧 |
| 日時 | `description.createdAt`、`publishedAt`（任意） | 商品の版番号・Unity への取り込み日時と同一視しない |
| 商品のバージョン | 確認モデルに専用フィールドなし | ファイル名・メモ等に利用者が記す可能性まで否定するものではない。機械的に読める統一仕様は未確認 |
| Unity プロジェクトへ導入済みか | 確認モデルに専用フィールドなし | KonoAsset への登録・ファイル所持と Unity への取り込みを区別する |

### 外部からの読み取り・ライセンス・互換性

- 公式ソースで通常のファイルとして JSON を開いて読む処理を確認できるため、同じ JSON を解析する読み取り連携の設計根拠がある。ただし、安定した外部読み取り API としての互換性保証は未確認。[K6](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/storage/src/json_asset_container.rs)
- 本体の LICENSE は MIT。コードを取り込む場合は著作権・許諾表示の条件を守る。配布ページはアイコンを別扱いとし、登録した商品の著作権は各権利者に帰属すると説明している。**MIT を、商品画像の再配布許可と解釈しない**。ローカル JSON 読み取りを禁止する条項は確認した本体 LICENSE にはないが、個別の商品画像の利用条件は未確認。[K1](https://booth.pm/en/items/6641548)[K15](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/LICENSE)
- 形式変更は実在する。アバター・衣装等のローダーには旧来の配列だけの形式、`version:2`、現在の `version:3` からの変換がある。その他アセットは確認ソースでは `version:3`。設定ファイルにも複数世代の変換がある。外部リーダーは未知の形式番号を拒否し、元データを変換・上書きしない設計が必要。[K5](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/loader/src/preferences.rs)[K8](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/loader/src/avatar.rs)[K9](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/loader/src/avatar_wearables.rs)[K10](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/loader/src/world_objects.rs)[K11](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/loader/src/other_assets.rs)
- 公式変更履歴では 1.2.6 にサムネイル取り込み時のリサイズ・JPEG 化が記載されている。画像の拡張子・サイズも恒久固定とみなさない。1.1.0 以降のデータ移行と `konoasset://addAsset` は確認できるが、これは商品一覧の照会 API とは別。エクスポート ZIP の厳密な互換仕様は未確認。[K16](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/CHANGELOG.md)

## 2. BOOTH Library Manager（BLM）

### 公式に確認できる範囲

BOOTH 公式は 2025-12-24 の告知で Windows 向けアーリーアクセスと Discord 内での配布を案内し、ファイル取り込み、カテゴリ・タグによる検索、リスト、プレビューを説明している。この告知だけから最新の配布バージョン、Mac 対応、正式版への移行状況、外部読み取り API は判断できない。いずれも本調査では未確認。[B1](https://booth.pm/announcements/893)

### 外部から読めるデータ

**以下は非公式拡張の実装で確認した内部形式であり、BLM 公式の公開スキーマではない。** 読み取りの実装例は存在するが、本調査では実 DB に対して実行していない。[B4](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelRuntime/LibraryRuntimeSnapshot.cs)[B5](https://github.com/32ba/unity-booth-library-manager-viewer/blob/master/Editor/BoothDatabaseReader.cs)

| 対象 | 公開された読み取り実装で確認できる内容 | 根拠 |
|---|---|---|
| メタデータ | Windows の `%APPDATA%/pm.booth.library-manager/data.db`。SQLite として読む。コードは `Environment.SpecialFolder.ApplicationData` から組み立てる | [B4](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelRuntime/LibraryRuntimeSnapshot.cs)[B5](https://github.com/32ba/unity-booth-library-manager-viewer/blob/master/Editor/BoothDatabaseReader.cs) |
| 実ファイルのルート | DB の `preferences.item_directory_path`。拡張は BLOB の場合 UTF-16LE として復号し、文字列の場合はそのまま使う | [B6](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelMaster/PreferenceMaster.cs) |
| 商品情報 | `booth_items` の商品 ID、`name`、`shop_subdomain`、`thumbnail_url`、`sub_category`、`published_at`、`updated_at` 等 | [B7](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelMaster/ItemMaster.cs) |
| ショップ・分類 | `shops`、`sub_categories`、`parent_categories`、`booth_item_tag_relations` 等を結合して利用 | [B8](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelMaster/LibraryMaster.cs) |
| 登録・更新履歴 | `registered_items.booth_item_id`／`created_at`、`booth_item_update_history.booth_item_id`／`last_updated_at` | [B9](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelMaster/RegisteredItemMaster.cs)[B10](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelMaster/ItemUpdateHistoryMaster.cs) |
| ダウンロード済みファイル | BLMImporter Core は `<実ファイルルート>/b<BOOTH商品ID>/` を再帰走査し、フォルダと unitypackage の有無を別々に判定 | [B4](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelRuntime/LibraryRuntimeSnapshot.cs)[B11](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelRuntime/ItemRuntime.cs) |
| 別の読み取り例 | 32ba の拡張は `registered_items.id` をフォルダ名に使う。Core の `b<ID>` と同一規則だと本調査では断定できない。手動登録品・旧形式を含む実データで要検証 | [B5](https://github.com/32ba/unity-booth-library-manager-viewer/blob/master/Editor/BoothDatabaseReader.cs) |
| 手動登録品 | `user_item_info` の `name`、`shop_name`、`thumbnail_filename` を読む例がある。BOOTH 商品 ID のない商品を含む | [B5](https://github.com/32ba/unity-booth-library-manager-viewer/blob/master/Editor/BoothDatabaseReader.cs) |
| サムネイル | BOOTH 商品は `thumbnail_url`。手動登録画像は DB と同じディレクトリの `thumbnails/<thumbnail_filename>` として解決する例がある。BOOTH 商品の既存ローカル画像キャッシュの完全な対応規則は未確認 | [B5](https://github.com/32ba/unity-booth-library-manager-viewer/blob/master/Editor/BoothDatabaseReader.cs)[B7](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelMaster/ItemMaster.cs) |
| 対応アバター | 確認したモデルに専用の対応アバター一覧は見当たらない。タグ・説明文と独立した公式項目があるかは未確認 | [B7](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelMaster/ItemMaster.cs)[B8](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelMaster/LibraryMaster.cs) |
| 商品の版番号 | 確認したモデルに意味的な版番号の専用フィールドはない。商品更新日時、ライブラリ更新日時、バリエーション情報は読めるが、ダウンロード済みファイルの版と一致する保証は未確認 | [B7](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelMaster/ItemMaster.cs)[B10](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelMaster/ItemUpdateHistoryMaster.cs)[B11](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelRuntime/ItemRuntime.cs) |
| Unity への導入状況 | DB の商品登録やダウンロード状態とは別。BLMImporter の導入判定は、unitypackage 内アセットと現在の Unity プロジェクトを照合して求めている | [B11](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelRuntime/ItemRuntime.cs)[B12](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/ImportedPackageIndex.cs) |

### 「Unity 内で検索・一括インポートする拡張」の現状と API

該当する機能を持つ **BLMImporter は公開済み**。作者の BOOTH 配布ページには 2026-06-13 の公開履歴があり、VPM 導入先と GitHub の公開パッケージがある。元の「公開予定」投稿自体の URL が HANDOFF にないため、**同じ投稿・同じ作者が指した製品であるかは未確認**。少なくとも、同種の拡張を公開待ちにする必要はない。[B2](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/README.md)[B13](https://booth.pm/zh-tw/items/8498096)

| 区分 | 確認結果 |
|---|---|
| BLM 公式 Unity 拡張 | 今回参照した公式告知で公開を確認できず、未確認。[B1](https://booth.pm/announcements/893) |
| BLM 公式外部 API／SDK | 公開仕様と互換性保証は未確認。内部 DB を読めることと公式 API の存在は区別する。[B1](https://booth.pm/announcements/893)[B4](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelRuntime/LibraryRuntimeSnapshot.cs) |
| 非公式 BLMImporter | UI パッケージと GUI 非依存の Core パッケージを分けて公開。検索・ローカルファイル列挙・インポート計画を扱える。[B2](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/README.md)[B14](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/README.md) |
| 非公式 Core API | `BLMImporter.Editor.Core.LibraryRuntimeSnapshot.Load(databasePath)`、`ItemFilter`、`PackageImporter`、`ThumbnailCache` 等がある。確認ソースでは商品一覧は `r_Items`、商品情報は `ItemRuntime.r_Master`。[B4](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelRuntime/LibraryRuntimeSnapshot.cs)[B7](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelMaster/ItemMaster.cs)[B11](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelRuntime/ItemRuntime.cs)[B14](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/README.md) |
| ドキュメントとのずれ | Core README のサンプルに `m_Items` 等の表記が残る一方、確認コードは `r_Items` 等に変わっている。サンプルのコピーだけでは済まず、採用する版のコードで照合する必要がある。[B4](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelRuntime/LibraryRuntimeSnapshot.cs)[B14](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/README.md) |
| サムネイル取得 | Core は URL から取得し、`Application.persistentDataPath/BLMThumbs` に独自キャッシュを作る。これは BLM 本体の画像保存場所ではない。[B14](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/README.md)[B15](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/ThumbnailCache.cs) |

### ライセンス・形式変更の扱い

BLMImporter 自体は MIT で、他の拡張から Core を利用する意図も明記されている。ただし **非公式拡張の MIT は BLM 本体の規約を代替しない**。BLM 本体の利用許諾に外部 DB 読み取りを許す／禁じる条項があるか、商品サムネイルの再利用条件、DB の互換性保証は未確認。製品組み込みの判断前に、本体配布時の規約と公式窓口の説明を確認する。[B13](https://booth.pm/zh-tw/items/8498096)[B14](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/README.md)[B16](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/LICENSE)

本体のバージョン別の DB スキーマ差分・移行履歴は未確認。異なる公開リーダーにフォルダ解決方式の違いがあることも踏まえ、特定のパス規則を全バージョンに適用しない。DB 列の検査、未知形式での連携停止、アプリ更新後の再検証を前提とする。[B4](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelRuntime/LibraryRuntimeSnapshot.cs)[B5](https://github.com/32ba/unity-booth-library-manager-viewer/blob/master/Editor/BoothDatabaseReader.cs)[B6](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelMaster/PreferenceMaster.cs)

## 3. かいへんナビ側の連携案（未実装）

### 現在の台帳との接点

ローカルの `Editor/Core/ItemLedger.cs` を確認した。保存先は `WorkFolders.Root + "/Ledger/items.json"`（`Assets/KaihenNavi/Ledger/items.json`）。`LedgerPackage` は `packageName`、`importedAt`、`scanned`、`commonFolder`、`assetPaths`、`prefabs` を持つ。現状は BOOTH ID・商品バージョン・サムネイルを保存していない。

`packageName` は Unity のインポートイベントから記録する。既存 Prefab のスキャンではフォルダ名からも生成するため、常に元の unitypackage ファイル名が復元できるわけではない。`scanned` は既存 Prefab の探索由来を示すフラグであり、外部アプリでのダウンロード済みフラグではない。

候補表示は `Editor/Core/ItemPicker.cs` の `SuggestionList` で `AssetPreview.GetAssetPreview(prefab)` と `GetMiniThumbnail(prefab)` を使い、`ItemLedger.PackageOf(assetPath)` で取り込み元名を表示する。この商品との接点を画像表示に利用する案とする。

ローカル根拠: [ItemLedger.cs](../Packages/com.suzuki-y0.jeni.kaihen-navi/Editor/Core/ItemLedger.cs)、[ItemPicker.cs](../Packages/com.suzuki-y0.jeni.kaihen-navi/Editor/Core/ItemPicker.cs)、[WorkFolders.cs](../Packages/com.suzuki-y0.jeni.kaihen-navi/Editor/Core/WorkFolders.cs)、[HANDOFF.md 4.6](../HANDOFF.md)。

### (a) 商品・パッケージ・Prefab の対応付け

次の優先順位を提案する。根拠となる外部キーとファイル情報は [K2](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/model/src/base.rs)[K13](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/storage/src/find.rs)[B4](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelRuntime/LibraryRuntimeSnapshot.cs)[B7](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelMaster/ItemMaster.cs)、現在の台帳項目は上記ローカルソースによる。

1. **明示的な商品 ID と選択した元ファイル**: 外部商品を選んで取り込む際に BOOTH 商品 ID、外部アプリ名、外部管理 ID、元ファイル名・識別情報と台帳の取り込み記録を結ぶ。ID は既存台帳に無いため、最初から全件を ID だけで自動対応できるわけではない。
2. **既存台帳はファイル名の一致から候補提示**: KonoAsset／BLM の商品配下で発見した unitypackage と `packageName` を、拡張子・大文字小文字等を整理して比較する。同名ファイル、アバター別版、旧版と新版が複数あるときは自動確定しない。
3. **商品名・フォルダ名は補助**: `commonFolder`、Prefab パスのフォルダ、商品名、作者名から候補を絞り、ユーザーが決める。作者共通フォルダ、言語違い、改名、複数商品の共有ファイルがあるため、類似文字列だけで紐付けない。
4. **必要な場合だけ内容を照合**: 元 unitypackage 内の GUID とプロジェクト内 GUID を比較する候補がある。BLMImporter に既存例があるが、共通依存物の GUID 一致や同じサイズだけでは商品・版の証明にならない。初期段階は手動選択を優先する。[B12](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/ImportedPackageIndex.cs)

提案する関係は「1 商品 → 複数の元パッケージ → 複数の Prefab」。BOOTH 以外・ID 未設定品には `source + externalAssetId` を使う。同じ BOOTH 商品を両アプリが管理していても、画像・タグ等の出典を残し、どちらの情報を採るか選べるようにする。

商品ページの更新日時、ライブラリ更新日時、Unity 取り込み日時、ファイル名に含まれる版文字列は別項目として扱う。版文字列を保存するなら取得方法も記録し、未取得は「不明」。更新日時だけで「最新版導入済み」と表示しない。[K2](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/model/src/base.rs)[B7](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelMaster/ItemMaster.cs)[B10](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelMaster/ItemUpdateHistoryMaster.cs)[B12](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/ImportedPackageIndex.cs)

### (b) サムネイルを候補表示に出す方法

- 対応が確定した商品のみ、KonoAsset は `images/<imageFilename>`、BLM は解決できたローカル画像または `thumbnail_url` を情報源とする。BLM 本体のキャッシュ規則が不明な画像は、任意の URL 取得で補う案。自動で全商品を再取得しない。[K6](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/storage/src/json_asset_container.rs)[B5](https://github.com/32ba/unity-booth-library-manager-viewer/blob/master/Editor/BoothDatabaseReader.cs)[B15](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/ThumbnailCache.cs)
- 外部画像は Unity の `Assets` に自動コピーせず、例えば `Library/KaihenNavi/Thumbnails/` に縮小キャッシュし、`Texture2D` として候補行に表示する案。台帳には画像バイナリを入れず、商品との対応と画像の出典・キャッシュキーを持たせる。マシン固有の絶対パスは Editor のローカル設定へ分離する。
- 画像が無い・壊れている・外部ドライブが外れている場合は既存の Prefab プレビューへ戻す。商品画像は商品全体、Prefab プレビューは個々の色・対応アバターの形を表すため、どちらを表示するか切り替えられると誤認を減らせる。
- 読み込んだテクスチャは上限を設けて破棄し、画面に必要な分だけ扱う。キャッシュの更新は出典の変更検知または明示的な再読込で行う。画像の共有・再配布は本調査で許諾を確認していないため、プロジェクト配布物へ同梱する前提にしない。[K1](https://booth.pm/en/items/6641548)[B16](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/LICENSE)

後半の配置・表示方式はかいへんナビの設計案であり、現状の機能ではない。

### (c) 両アプリを使わない人への影響を無くす条件

連携は初期状態で無効にする。連携設定を使う人だけ保存先を選び、アプリや DB が無いときは既存の台帳・検索・Prefab プレビューで完結させる。初回起動で外部アプリの必須インストールやアカウント認証を要求しない。

BLMImporter Core を採用する場合も別の任意連携パッケージに分ける等、本体から必須参照しない。未導入環境で asmdef の参照エラーを出さないことを検証する。外部データの読み取りに失敗しても台帳を空で上書きせず、元の衣装・アクセ候補を維持する。これは今後の実装要件で、現状への変更は行っていない。

## 4. 難しさ・危うさと推奨する順序

| 作業 | 相対的な難しさ | 危うさ／検証すること |
|---|---|---|
| KonoAsset JSON 読み取り | 小〜中 | JSON の形式番号、欠損値、画像の有無。公式ソースを根拠にできるが、配布版と開発版の一致は要確認。[K6](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/storage/src/json_asset_container.rs)[K8](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/loader/src/avatar.rs) |
| 既存台帳の自動対応付け | 大 | 既存台帳に商品 ID・元の絶対パスが無い。同名・旧版・共通フォルダで誤対応する。手動確定を残す |
| BLM DB 自前読み取り | 中〜大 | 公開互換仕様が未確認。SQLite のロック／WAL を含む更新中の整合性に配慮し、読み取り専用接続・整合したスナップショットで評価する。DB 本体だけの無条件コピーに依存しない |
| BLMImporter Core 採用 | 中 | 自前の SQL を減らせる一方、拡張の API 変更・README のずれ・本体対応範囲に依存。Core の版を固定し、ライセンス表示と依存を確認。[B3](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/package.json)[B4](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelRuntime/LibraryRuntimeSnapshot.cs)[B14](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/README.md)[B16](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/LICENSE) |
| 画像表示 | 中 | 画像サイズ・形式・キャッシュ容量・削除済み画像・URL 切れ・ネットワーク失敗。元アプリの画像は変更しない |
| 商品の版・導入判定 | 大 | ID は商品を示すがファイルの版までは示さない。日時や GUID の部分一致だけでは確定できない。[B7](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelMaster/ItemMaster.cs)[B12](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/ImportedPackageIndex.cs) |
| Windows／Mac・保存先移動 | 中〜大 | ドライブ文字、区切り文字、大文字小文字、日本語・Unicode、Documents の移動、NAS・外部ディスクを考慮。OS に適したパス結合とユーザーによる再指定を用意。両アプリの Mac 公式サポートは未確認。[K3](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/tauri/src/lib.rs)[K14](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/tauri/tauri.conf.json)[B1](https://booth.pm/announcements/893) |

難易度は工数見積もりではなく、この調査に基づく設計上の評価。SQLite・パス対策も提案であり、本調査で障害を再現したという意味ではない。

推奨する進め方:

1. 配布済み KonoAsset の版とデータ形式を実データの小さなサンプルで確認。ユーザー指定ルートから読み取るだけの検証を行う。アプリを閉じた状態のコピーで始め、元データは書き換えない。
2. 手動の商品選択 → 対応の保存 → サムネイル表示を検証。既存プレビューへの復帰と、連携未導入時の動作を先に確認する。
3. ファイル名等による候補提示を加える。確定前に画像・商品名・ショップ・元ファイルを確認できるようにする。
4. BLM 本体の現行配布版・利用規約・スキーマと BLMImporter Core の互換性を確認。任意アダプターとして追加し、元 DB の変更やダウンロード自動化は連携の初期範囲に入れない。
5. 商品バージョン表示・高度な導入判定は別段階とする。正確な元ファイルを特定できない既存記録は「不明」を維持する。

## 根拠 URL

KonoAsset のコードリンクは、断りのない限り上記の公式リポジトリの確認コミットを指す。

- [K1] [公式配布ページ（商品説明・配布版・利用条件）](https://booth.pm/en/items/6641548)
- [K2] [公式の商品モデル `model/src/base.rs`](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/model/src/base.rs)
- [K3] [初期パス・設定の読み込み `tauri/src/lib.rs`](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/tauri/src/lib.rs)
- [K4] [設定モデル `model/src/preference.rs`](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/model/src/preference.rs)
- [K5] [設定の形式番号と旧形式変換 `loader/src/preferences.rs`](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/loader/src/preferences.rs)
- [K6] [JSON の読み書き・画像参照 `storage/src/json_asset_container.rs`](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/storage/src/json_asset_container.rs)
- [K7] [商品種別別のファイル名 `model/src/asset_trait.rs`](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/model/src/asset_trait.rs)
- [K8] [アバターの形式番号 `loader/src/avatar.rs`](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/loader/src/avatar.rs)
- [K9] [衣装等の形式番号 `loader/src/avatar_wearables.rs`](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/loader/src/avatar_wearables.rs)
- [K10] [ワールドアセットの形式番号 `loader/src/world_objects.rs`](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/loader/src/world_objects.rs)
- [K11] [その他アセットの形式番号 `loader/src/other_assets.rs`](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/loader/src/other_assets.rs)
- [K12] [保存先・データ移動処理 `storage/src/asset_storage.rs`](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/storage/src/asset_storage.rs)
- [K13] [unitypackage の走査 `storage/src/find.rs`](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/crates/storage/src/find.rs)
- [K14] [アプリ識別子・配布設定 `tauri.conf.json`](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/src-backend/tauri/tauri.conf.json)
- [K15] [本体 LICENSE（MIT）](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/LICENSE)
- [K16] [公式変更履歴](https://github.com/siloneco/KonoAsset/blob/f83271e53f276df4bb0b60865cfbf86b42f1bbc4/CHANGELOG.md)

BLMImporter のコードリンクは、拡張作者の確認コミットに固定した。これらは BLM 本体の公式資料ではない。

- [B1] [BOOTH 公式「BOOTH Library Manager」アーリーアクセス開始のご案内](https://booth.pm/announcements/893)
- [B2] [BLMImporter 作者の公開リポジトリ・導入案内](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/README.md)
- [B3] [BLMImporter Core の版・Unity 要件](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/package.json)
- [B4] [DB パス・ライブラリ構築 `LibraryRuntimeSnapshot.cs`](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelRuntime/LibraryRuntimeSnapshot.cs)
- [B5] [32ba の別の読み取り実装 `BoothDatabaseReader.cs`](https://github.com/32ba/unity-booth-library-manager-viewer/blob/master/Editor/BoothDatabaseReader.cs)（このリンクは可変ブランチ）
- [B6] [保存先の復号 `PreferenceMaster.cs`](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelMaster/PreferenceMaster.cs)
- [B7] [商品項目 `ItemMaster.cs`](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelMaster/ItemMaster.cs)
- [B8] [読み取り対象テーブル `LibraryMaster.cs`](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelMaster/LibraryMaster.cs)
- [B9] [登録日時 `RegisteredItemMaster.cs`](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelMaster/RegisteredItemMaster.cs)
- [B10] [更新履歴 `ItemUpdateHistoryMaster.cs`](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelMaster/ItemUpdateHistoryMaster.cs)
- [B11] [ファイル所持状態 `ItemRuntime.cs`](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/Model/ModelRuntime/ItemRuntime.cs)
- [B12] [Unity プロジェクトとの照合 `ImportedPackageIndex.cs`](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/ImportedPackageIndex.cs)
- [B13] [BLMImporter 作者の BOOTH 配布ページ](https://booth.pm/zh-tw/items/8498096)
- [B14] [BLMImporter Core API 呼び出しガイド](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/README.md)
- [B15] [拡張独自の画像キャッシュ `ThumbnailCache.cs`](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/Packages/com.mirukudasi.blm-importer-core/Editor/Core/ThumbnailCache.cs)
- [B16] [BLMImporter の LICENSE（MIT）](https://github.com/mirukudasi/BLMImporter/blob/c8b6533002405378dce5b756c257bf95b547f66b/LICENSE)
