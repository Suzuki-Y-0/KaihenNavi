# かいへんナビ 引き継ぎメモ（2026-10-11 Unity 実機確認後）

次の作業者（人でも AI でも）が、これだけ読めば続きから始められるようにまとめたもの。

## 1. これは何か

VRChat 初心者向けの Unity エディタ拡張「かいへんナビ」（仮称）。BOOTH で販売予定。
Unity 標準機能を「やりたいこと」単位のかんたんな画面で包み、ヒエラルキーやインスペクターを触らずに見た目を改変できるようにする。

- 企画・引き継ぎ資料（市場調査、競合、機能一覧、画面仕様、技術設計、販売戦略）:
  https://claude.ai/code/artifact/9b4542aa-308b-43b6-ac15-3ce195893895
- リポジトリ: https://github.com/Suzuki-Y-0/KaihenNavi （Private、main ブランチ）
- 対象: BOOTH でアバターを買ったが Unity 操作が難しい一般ユーザー
- 価格案: 無料体験版（3 機能）＋製品版 1,500〜2,500 円

## 2. いまの状態

体験版の 3 機能と土台は実装済みで、**2026-10-11 に Unity 2022.3.22f1 の実機で動作確認した**。
確認環境: VCC プロジェクト（VRChat SDK Avatars 3.10.4 / Modular Avatar 1.18.7 / lilToon 2.3.4 / Gesture Manager 3.9.8）、アバターは Cazalis（Cazalis_Kisekae.prefab）1 体＋猫耳アクセ（ear / earL / earR）。
Mono と自作スタブでの 3 通りのコンパイル確認（`tools/check.sh`）も引き続き通る。

| 項目 | 状態 |
| --- | --- |
| パッケージ雛形（package.json、Editor 専用 asmdef、固定 GUID の .meta） | 済・Unity 確認済み（コンパイルエラー 0） |
| EditorWindow（Tools > かいへんナビ） | 済・Unity 確認済み（アバター自動検出、日本語・矢印の表示 OK） |
| 自動バックアップ／元に戻す／履歴 | 済・Unity 確認済み（3 機能をまたいで最初の状態まで完全に戻る。ドメインリロード後も件数を保持） |
| 機能1 色変え | 済・Unity 確認済み（複製先に保存、元マテリアルはハッシュで無変更を確認） |
| 機能2 脱ぎ着メニュー生成（MA） | 済・Unity 確認済み（MA コンポーネントの設定、シーン上のプレビュー、Play 時の MA 処理まで）。Gesture Manager での切り替え操作だけ未確認 |
| 機能6 位置と大きさ | 済・Unity 確認済み（移動量・拡大率どおりに動き、元に戻すで元の値へ） |
| 日英中韓の言語ファイル | 済（日本語のみ Unity で表示確認） |
| 製品版の残り 7 機能 | 未着手（カードだけ鍵付きで表示） |
| VPM 配布・unitypackage・紹介動画・説明書 | 未着手 |

## 3. Unity での動作確認の結果（2026-10-11）

### 確認できたこと

- コンパイル: Console の赤エラー・警告 0。`Jeni.KaihenNavi.Editor.dll` が生成され、`KAIHENNAVI_MA` も有効（脱ぎ着画面が MA 版になる）
- 色変え: 髪のスライダーで `Assets/KaihenNavi/Cazalis_Kisekae/Materials/` に複製ができ、シーンの見た目が変わる。File > Save Project 後の .mat に `_MainTexHSVG` が書かれる。lilToon の FakeShadow（色調補正なし）は設計どおり `_Color` 側が変わる。元アセット 99 ファイルの md5 は全工程で無変更
- 脱ぎ着: `KaihenNavi_Menu` の下に MA Menu Item（Toggle、`KaihenNavi/<名前>`、既定 ON・保存・同期）と MA Object Toggle が正しく付く。対象を非表示にしても **MA のプレビューでシーン上は服が見えたまま**（懸念は問題なし）。Play で MA がメニューを処理し、服が既定 ON で表示される
- 位置と大きさ: Head に付けた猫耳を「上」×3 で Y が 0.121→0.136（0.005×3）、+5%×2 で 0.77→0.847（110%）。元に戻すで元の値に戻る
- 元に戻す: 「メニューを作り直す → 元に戻す」で前のメニュー（ブラ＋ショーツ）が復元。さらに戻すとメニューが消え、マテリアル参照も `Assets/Cazalis/Material/` の元マテリアルに戻る。手で追加した猫耳は消えない
- 4 章の前提（MA 1.18.7 の API 名・asmdef 名、`PopupField` の名前空間、矢印と日本語の表示、`_MainTexHSVG`）はすべて問題なし
- Play 終了時に出た赤エラー 3 件（`TreeViewController.IsItemDragSelectedOrSelected` の NullReference、GUIClip）は Unity 本体の Hierarchy 由来で、かいへんナビとは無関係

### 直したこと

- ヘッダー: 狭いウィンドウでタイトル・バッジと言語選択が重なっていた → 折り返し可能にし、ヘッダー／フッターが縦に押しつぶされないようにした
- 本文の余白: 2022.3 の ScrollView は中間要素を挟むため、子セレクタ（`>`）指定の余白が効いておらず内容が左端にくっついていた → 子孫セレクタに変更
- 脱ぎ着: 通信量（bit）がチェックを変えても更新されなかった → チェックのたびに数え直す
- 脱ぎ着: 「元に戻す」やメニュー作り直しのあとも、チェックが前の手操作のまま残っていた → 実際のメニューの中身が変わったら、覚えたチェックを捨ててメニューに合わせる
- 脱ぎ着: 小物が中のメッシュ名（「L」「R.001」など）で並んでいた → 小物のまとまりにメッシュが 1 つだけなら、まとまりの名前（「earL」など）を表示名にし、元のオブジェクト名を小さく添える
- 文字: Unity エディタの標準フォントには日本語が無く、文字ごとに別のフォントへ切り替わって漢字とかなの太さがばらばらだった → `FontLoader` で言語ごとに Windows 標準の UI フォントを 1 つ当てる（日本語 Yu Gothic UI、中国語 Microsoft YaHei UI、韓国語 Malgun Gothic、英語 Segoe UI。他言語の文字は代替フォントで補う）。実行時に作ったフォントでは USS の bold を Unity が太らせないため、太字指定の文字には同じ書体の Bold を直接当てている（0.1 秒ごとに新しく出来た文字を見直す）。フォントはパッケージに含めない
- カード: やりたいことを選び直すたびに②のカード一覧を全部作り直しており、太字フォントが当たる前の状態が一瞬見えて見た目が変わっていた → 選択中の印（`kn-card--selected`）だけ付け替える

### まだ確認できていないこと（次にやる）

1. Gesture Manager のラジアルメニューから脱ぎ着を実際に切り替える（リモート操作ではインスペクターのクリックが Play 中に効かず未確認）
2. 人気アバターをあと 4 体ほど（マヌカ、桔梗、セレスティア、ルルネなど）。Cazalis は目のマテリアルが「目 0」になった（目が顔マテリアルに含まれる型）。ほかのアバターでの部位判定を見る
3. MA が入っていないプロジェクトでのコンパイルと導入案内（`tools/check.sh` の NoMA では通っている）
4. ピンク色や Missing があるアバター
5. 英語・中国語・韓国語の表示
6. 最終版 USS（ヘッダー／フッターの `flex-shrink: 0`）の見た目（折り返し自体は確認済み）

## 4. 気づいた改善点（未着手）

- 「元に戻す」で最初の状態まで戻しても、`Assets/KaihenNavi/<アバター名>/Materials/` の複製マテリアルは残る（参照はされない）。掃除するかは要検討
- 脱ぎ着の候補に素体の一部（Body_Base）や髪も並ぶ。初心者には多すぎるので、髪・素体は折りたたむなどしたい
- ヒエラルキーにアクセを足しても位置と大きさの候補は自動では増えない（「更新」かカードの選び直しが必要）。`hierarchyChanged` で候補だけ取り直すとよい
- テストに使ったプロジェクトは「自動リフレッシュ」がオフで、ファイルを差し替えたら Ctrl+R が必要だった（ツールの不具合ではない）

## 5. 設計の要点

- **元アセットは絶対に書き換えない**: マテリアルは初回変更時に `Assets/KaihenNavi/<アバター名>/Materials/` へ複製し、アバター内の参照をすべて差し替える（`MaterialCopier`）
- **自動バックアップ**: 「やりたいこと」を選んで最初に何か変える直前に 1 件保存（`BackupService.BeginEdit`）。同じ機能を続けて触っている間は追加しない。保存先は `Assets/KaihenNavi/<アバター名>/Backups/*.asset`、最大 20 件
  - 保存内容: 全 Transform（位置・回転・大きさ・表示）、全レンダラーのマテリアル参照、ツールの複製マテリアルの中身（サブアセットに複製）、BlendShape、View Position、脱ぎ着メニューの定義（`IBackupParticipant` で JSON）
  - オブジェクトは参照ではなく「名前#同名内の番号」のパスで記録（`HierarchyPath`）
  - 元に戻すと、そのバックアップ以降に作られたオブジェクトを消してから状態を戻し、使ったバックアップは削除。Unity の Ctrl+Z でも取り消せる
- **Editor 専用**: アップロードされるアバターに独自スクリプトは残さない。脱ぎ着は MA 標準コンポーネントのみ使用
- **Undo**: スライダーのドラッグ中の変更は `UndoHelper` で Ctrl+Z 1 回分にまとめる
- **体験版／製品版**: スクリプト定義シンボル `KAIHENNAVI_FULL` があると鍵が外れる（`Edition.IsTrial`）。`Edition.BoothUrl` は仮の `https://booth.pm/`
- **言語**: `Editor/Localization/{ja,en,zh-Hans,ko}.json`（キーと文言だけの平らな JSON）。文言を足したら 4 ファイルともキーをそろえる（`tools/check.sh` が検査）

## 6. ファイル構成

```
Packages/com.suzuki-y0.jeni.kaihen-navi/
  package.json / CHANGELOG.md
  Editor/
    Jeni.KaihenNavi.Editor.asmdef
    Core/  KaihenNaviWindow（画面）, BackupService / BackupSnapshot（バックアップ）,
           MaterialCopier, PartGuesser（髪・服・目・肌の判定）, AvatarFinder, HierarchyPath,
           WorkFolders, UndoHelper, L10n, Ui, PackagePaths, Edition
    Features/  FeatureBase, FeatureCatalog（カードの並び）, ColorFeature, ToggleFeature, TransformFeature
    UI/KaihenNavi.uss
    Localization/*.json
tools/
  check.sh      Unity なしのコンパイル確認（apt install mono-mcs が必要）
  gen_meta.py   新しいファイルを足したら実行して .meta を作る（GUID はパスから固定生成）
  stubs/        確認用の API スタブ（配布物には含めない）
```

新しい機能は `FeatureBase` を継承し、`FeatureCatalog` の `PlannedFeature` を置き換える。何かを変える直前に `ctx.BeginEdit()`、オブジェクトを作ったら `ctx.NoteCreated()` を呼ぶ。

## 7. 残りのタスク（資料の順）

1. Unity での動作確認と修正（Cazalis 1 体では完了。残りは 3 章「まだ確認できていないこと」）
2. 紹介動画（30 秒程度）と説明書、BOOTH で無料体験版を公開
3. 配布: [jeni-tools-vpm](https://github.com/Suzuki-Y-0/jeni-tools-vpm)（ID `com.suzuki-y0.jeni`）に追加するか検討＋unitypackage。ただし有料版の配布方法は別途決める
4. 反応を見て製品版の 7 機能を追加（3 肌隠し、4 体型、5 身長、7 視点、8 ピンク修正、9 明るさ、10 サムネイル）
5. 知恵袋や X の質問を集め、機能の優先順位を見直す

## 8. 競合メモ（2026-10-11 に BOOTH を再検索）

資料作成後に近いものが出ている。統合（3 機能を 1 画面で）と自動バックアップで差別化する方針は維持。

- いめちぇん！（無料、lilToon 専用の色変え、複製してから変更）: https://booth.pm/ja/items/8788853 … 色変え単体ではほぼ競合
- MO-DERA（無料＋支援版、手順を案内するアシスト。自動では変更しない）: https://booth.pm/ja/items/8966216
- DESIREVO WARDROBE（800 円、衣装・マテリアル・メニュー設定）: https://booth.pm/ja/items/8854279
- AvatarOutfitMenuBuilder（無料、衣装切替メニュー自動生成）: https://booth.pm/ja/items/8961880
- Avatar Doctor（500 円〜、初心者向け診断）: https://booth.pm/ja/items/8797619

## 9. 作業環境メモ

- クラウドのセッションからは GitHub に新規リポジトリを作れない（既存リポジトリへの push は可）
- リモート URL は大文字小文字込みで `https://github.com/Suzuki-Y-0/KaihenNavi.git`
- コミット末尾に Co-Authored-By などの署名行を付けている
- 動作確認に使ったプロジェクト: `C:\Users\NextG\Documents\VRChatCreatorCompanion\VRChatProjects\MCP Project`（`Packages/com.suzuki-y0.jeni.kaihen-navi` にコピー済み。シーンは未保存の Untitled）
- Unity Hub はサインイン画面で止まるので、ALCOM の「Unity を開く」から起動した
