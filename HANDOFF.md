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
**2026-10-19**: 改変アバター・服・アクセと製品版の機能をすべて実装し、Unity で確認したうえで `feature/my-avatar` を `main` にマージした。
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
| 改変アバター（元アバター → ベース → 改変アバターの 2 段）・服・アクセ | 済・Unity 確認済み（4.4） |
| 製品版の機能（体型・身長、肌、視点、ピンク、明るさ、サムネイル、動き、メニュー整理） | 済・Unity 確認済み（4.4 と各節。未確認の点は各節に記載） |
| アップロード前のチェック（`UploadCheck`）・「今すぐ保存」ボタン | 済・Unity 確認済み（2026-10-21。4.9 参照） |
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
- カード: それでも②が動いていた。原因は 2 つ。選択中だけ枠線が 2px（通常 1px）で、中の文字の幅が 2px 狭まり折り返しが変わってカードの高さが変わっていた → 通常は余白 9px、選択中は 8px にして中身の大きさを同じにした。もう 1 つは③が短い項目に切り替えたときのスクロール位置の巻き戻り → ③に画面の高さぶんの最小高さを持たせた

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

## 4.4 Unity での動作確認（2026-10-19 夜、Unity 2022.3.22f1・MA 1.18.7・Cazalis）

製品版の機能は、PC 側の `Edition.cs` だけ一時的に製品版にして確認し、終わってから元に戻した（体験版に戻ったことも確認）。
元アバター（Cazalis.prefab・Cazalis_Kisekae.prefab・マテリアル 27・コントローラー 11）の md5 は、作業の前後で変わっていない。

**確認できたこと**
- 新規作成: `Bases/Cazalis/Cazalis Base.prefab`（元アバターのバリアント）と `Avatars/Cazalis 改変/`（ベースのバリアント）ができ、avatar.json の kind・親も正しい。カードは 10 枚
- ベースにつなぎ直す（テスト用スクリプトで確認し、確認後に削除）: GUID はそのまま、親がベースになり、改変（子の追加・頭の大きさ）とブループリント ID が残る。シーンに置いた改変アバターもつながったまま。前のものは Backups にコピーされ、一覧には出ない
- シーンのアバターを改変アバターにする（`Create`）: ベースを通る 2 段になり、シーンでの改変も残る
- 明るさ: lilToon をアイテムごとに一覧し、「暗い場所でも見やすく」で 8 個のマテリアルが Materials に複製され `_LightMinLimit` が 0.25 に。元のマテリアルは不変。影オフの案内も出る
- 体型: 影絵とボーン図が出る。頭を選んでスライダー → `ModularAvatarScaleAdjuster.Scale` が改変アバターに保存される
- サムネイル: 顔アップ・上半身・全身ともきれいに撮れて `Thumbnail.png` に保存
- 視点: 自動で合わせると 高さ 1.229 m（作者の設定 1.230 m）
- ピンク: 「見つかりませんでした」と正しく出る
- アクセ: 一覧から猫耳を選んで頭に付けられた（位置も頭の上）
- 服: MONOLITH を着せられ、着ている服の一覧と「脱がす」が出る
- 肌: 「○○を着ているときだけ」で `KaihenNavi_SkinHide` に Shape Changer（Body_Base / Shrink_UpperArm / Set / 100）。「元に戻す」で消える
- Mesh Cutter: 型名どおり見つかり、置ける
- MA の API 名（ShapeChanger.Shapes・ChangedShape・ShapeChangeType・ScaleAdjuster.Scale・MeshCutter）はすべて記憶どおりだった

**直したこと**
- 本文がスクロールしなかった: 体型機能のクラス名 `kn-body` が本文の ScrollView のクラスと同じで、`flex-direction: row` が本文に効いていた → `kn-figure` に変更
- 服: メニュー経由の Setup Outfit は対象が渡らず MA が「オブジェクトが選択されていません」を出していた → MA の公開 API `SetupOutfit.SetupOutfitUI(GameObject)` を名前で探して呼ぶ（無ければメニュー）
- 肌: 顔（Body）を肌として選んでいた（Cazalis は顔が Body、体が Body_Base）→ 肌のメッシュを選べるようにし、最初は縮める用のシェイプキーがいちばん多いメッシュにする
- Mesh Cutter: 切り取る相手（肌のメッシュ）を最初から入れておく
- 身長がメッシュの外枠で測っていて 161 cm と大きめに出ていた → 今のポーズで頂点を計算して測る（全身サムネイルの余白も減る）
- 服・アクセ: ドラッグが難しい人向けに「または一覧から選ぶ」（Unity 標準の選択画面）を追加

**まだ確認していないこと**
- VRChat SDK でのビルド・アップロード、服を着せた状態の見た目（今回の服は Shinano 用なので体に合っていない）
- 体型の影絵を直接ドラッグする操作、身長を変えたときの視点の追従
- 「歩き方」などベースの設定を変えたときに子の改変アバターへ反映されるか（ベースを書き換える機能はまだ無い）

### 10-19 夜の追加確認と修正

- 体型: 身長スライダーで Armature の大きさが変わり、視点も同じ割合で追従（1.2289 → 1.2500、101.72%）。身長の表示が変わらなかった（2 秒の覚えに引っかかっていた）→ 変えたあとに測り直す。着せた服（Merge Armature 入り）は、アップロードまで元の大きさで置かれているので身長から除く
- 身長はアクセも含めて測る（猫耳を付けていると、その先端まで）。今の確認用アバターは猫耳が頭より 15 cm ほど上にあり、身長が大きめに出る
- 空の `Textures/` を毎回作るのをやめた（使うときに作る）。README.md にも `Menus/` を追記
- Unity のコンソール（LogEntries を一時スクリプトで書き出し）に、かいへんナビのエラーは無し

### 10-19 深夜：製品版の残りの確認（MCP Project / Cazalis 改変、一時的に製品版にして確認）

- ピンク: 改変アバターの `Cazalis_Pumps.mat` のシェーダーを一時的に存在しない GUID にすると、「『lilToon』が入っていません」と Cazalis_Pumps / Cloth_Pumps、入手先ボタンが出た。戻して「もう一度調べる」→「見つかりませんでした」
- 改変アバター一覧: Cazalis 改変（ベースの下にある）には「ベースにつなぎ直す」が出ない（正しい）
- 別バージョンを作る: 「Cazalis 改変 コピー」ができ、avatar.json の親はベース、アップロード先 ID は空
- 名前の変更: 一覧で名前を書き換えて Enter → Prefab・フォルダ・avatar.json の名前がそろって変わった
- 別バージョンを開いて明るさのおすすめを押すと、元の改変アバターと共有していたマテリアルはそのまま、別バージョン用に `Materials/` へ新しく複製された（元の改変アバターの値は変わらない）
- 開くと他のアバターは隠れる。シーンから消すと選択画面に戻る
- 確認に使ったもの（別バージョン、マテリアルの変更）はすべて消して元に戻した。Edition.cs も元に戻し、コンソールにエラーなし
- まだ: ロックされた（Shader の入っていない）マテリアルでの明るさ、視点マーカーの見た目

### メニューを整理したい（`MenuFeature`・製品版、10-19 実装）

- 「メニューを整理できるようにする」で、元のメニュー（サブメニューも 8 段まで）を `KaihenNavi_AvatarMenu`（MA Menu Installer + Menu Group）の下に MA Menu Item として作る。オブジェクト名は元の場所の番号（"3.1" など）、表示名は label。アバター設定のメニューは `Avatars/<名前>/Menus/EmptyMenu.asset` に差し替える（MA の Extract Menu は元のメニューの隣に空のメニューを作るので使わず、自前で作る）
- 使わない＝`KaihenNavi_HiddenMenu`（EditorOnly・メニュー部品なし）の下へ移す。MA の `MenuNodesUnder` は子の MenuSource しか見ず、**非表示の Menu Item も普通に出してしまう**（active を見ない）ため、隠すのではなく置き場所を変える
- 並べ替え（↑↓）、名前の変更（FocusOut）、「その他へ」（`others` サブメニュー）。使わない項目には「オンのまま固定」など、パラメータの初期値から固定される状態を出す
- 「元に戻す」用に `MenuTidyBackup`（元のメニューのパスと項目ごとの変更を JSON にして、戻すときに作り直す）。「元のメニューに戻す」で取り出したメニューを消して設定を元のメニューに戻す
- 確認済み（テスト用スクリプトで NDMF の `AvatarProcessor.ProcessAvatar` まで通して、出来上がったメニューの中身を確認）: 使わない項目が消える、「その他」に入る、名前の変更が出る、変更を JSON にして作り直しても同じになる。元のメニューのファイル 9 個は無変更
- **改変アバターでの形の変更はファイルの中で行う**: 取り出したメニューは自動保存で改変アバター（Prefab）の一部になり、シーンでは親の付け替え・並べ替えが Unity に拒否される（最初の実装はチェックを外しても何も起きなかった）。隠す・並べ替え・「その他」は `Restructure` で、いったん保存してから `LoadPrefabContents` の中で変える。Ctrl+Z は効かないが、変更前の自動バックアップで「元に戻す」できる。名前の変更はシーンの上書きで足りる
- 画面でも確認済み: 名前の変更（label に入る）、取り出し（アイコン付きで表示）、使わない（下に「使わない項目」として出る）、使うに戻す（元の位置へ）、「その他へ」、↓での並べ替え、「元のメニューに戻す」（設定が元のメニューに戻る）

### 動きを変えたい（`WalkFeature`・製品版、10-19 実装・Unity で確認）

- 歩き方（Base）・座り方（Sitting）・エモート/AFK（Action）を選べる。置くオブジェクトは `KaihenNavi_Walk` / `KaihenNavi_Sit` / `KaihenNavi_Action`。FX（表情・メニューの切り替えが全部消える）と Gesture（手の形）は出さない。座り方の差し替えと戻しも Unity で確認済み

- アバターの設定（Base 層）は直接書き換えない。`KaihenNavi_Walk` に MA Merge Animator（layerType = Base、mergeAnimatorMode = Replace、pathMode = Absolute）を付けて置くだけ。MA 1.18.7 の `RemoveLayerPass` が Replace を見て Base 層を空にしてから合成する（隠したオブジェクトも数えるので、隠しても無効にはならない）
- 「このアバターだけ」= 改変アバターの中に置く（Undo・バックアップ対応）。「みんな」= ベースを `LoadPrefabContents` で開いて置く（Ctrl+Z 不可なので確認ダイアログ）
- 置き換えが 2 つあると MA が `error.merge_animator.multiple_replacements` で止めるので、重なりを検出して「重なりを直す」（このアバターで足した方を消し、そのコントローラーをベースから来た部品への上書きにする）
- このアバターだけ戻す: ベースから来た部品への上書きは、いったん保存してから改変アバターのファイル内で `RevertObjectOverride`
- MA 用の歩き方 Prefab（GoGo Loco など）は中に入れるだけ。中に Merge Animator が無い Prefab は断る
- 一覧から選ぶ欄（ObjectField）は 1 回クリックで値が入るため、「これにする」ボタンを押すまで使わない（服・アクセも同じ `ItemPicker.PickRow`）
- 確認済み: このアバターだけ → みんな → 重なりを直す → このアバターだけ戻す → みんな戻す、の一連でファイルが期待どおりになる
- 未確認: 実際に歩き方が変わるか（再生・アップロード）。GoGo Loco の Prefab での動作

## 4.5 改変アバター・服・アクセ（2026-10-19 追加・4.4 で確認）

ブランチ `feature/my-avatar`。ターゲットは「Unity の標準の仕組み（ヒエラルキー、Prefab、バリアント）を触るのが苦手な初心者」。画面にはヒエラルキー・ボーン・バリアント・Setup Outfit などの言葉を出さない。

### ①アバターをえらぶ＝二択から始める（`AvatarChooser`）

- 「新しく改変アバターを作る」: プロジェクト内の元アバター（Avatar Descriptor 付き Prefab、`MyAvatar.BaseAvatars`）から選び、名前を付けて「作る」。プレハブバリアントと決まった形のフォルダを自動で作り、シーンに出す（ほかのアバターは隠す）
- 「前に作った改変アバターを直す」: `Assets/KaihenNavi` 以下の改変アバター一覧（サムネイル付き）から選ぶ。シーンにあればそれを表示、なければ出す。製品版のみ「別バージョンを作る」「名前を変える」
- シーンに直接置いた、改変アバターになっていないアバターも「シーンにあるアバター」として選べる。選ぶと①に「改変アバターにする（おすすめ）」を出す（「あとで」も可）
- アバターが決まるまで②③は隠す。決まったら①は「いま改変しているアバター：名前」と自動保存の状態、「ほかのアバターにする」

### 決まったフォルダ構造（`WorkFolders`）

```
Assets/KaihenNavi/
  README.md                この形の説明（ほかのツールや人向け。形が変わったら書き直す）
  Bases/<元アバター名>/
    <元アバター名> Base.prefab  改変のベース（元アバターのプレハブバリアント。元アバターごとに 1 つ）
    avatar.json            kind = "base"
  Avatars/<名前>/
    <名前>.prefab          改変アバター（ベースのプレハブバリアント）
    avatar.json            ほかのツール向けの説明（format, kind, tool, name, prefab, basePrefabPath/Guid＝元アバター, parentPrefabPath/Guid＝直接の親, createdAt）
    Thumbnail.png          サムネイル機能で撮ったもの
    Materials/  Textures/  Backups/
```

- **2 段のバリアント（10-19 ユーザー提案）**: 元アバター → ベース → 改変アバター。「新しく作る」で元アバターを選ぶと、ベースが無ければ作り（ブループリント ID は外す）、そのバリアントとして改変アバターを作る。歩き方（アニメーションコントローラー）など「この元アバターから作るもの全部に共通」の変更はベースに入れれば全員に効く（改変アバター側で同じ所を変えていればそちらが優先）。元アバターの更新もベース経由で全員に届く
- ベースは `IsMinePath` に含めない（＝自動保存の書き込み先にならない、「前に作った」一覧にも出ない）。ベースを書き換えるのは「共通の設定を変える」操作だけにする予定（`LoadPrefabContents` → 変更 → `SaveAsPrefabAsset`。影響する改変アバターは `MyAvatar.ChildrenOf` で出せる）
- **ベースへのつなぎ替え（Unity 標準の「Prefab の置き換え」= `PrefabUtility.ReplacePrefabAssetOfPrefabInstance`、2022.2 以降）**: (1) シーンのアバターを「改変アバターにする」（`MyAvatar.Create`）ときは、買ったときの Prefab につながっていれば先にベースへ差し替えてから保存するので、新規作成と同じ 2 段になる。(2) 古い改変アバター（元アバターの直接のバリアント）は「前に作った」一覧に「ベースにつなぎ直す」ボタンを出す（`MyAvatar.Rebase`）: 一時インスタンス → `UnpackPrefabInstance(OutermostRoot)` で「元アバターのインスタンス＋改変」に戻す → ベースへ差し替え（`ObjectMatchMode.ByHierarchy`、オーバーライドは残る）→ 同じパスへ `SaveAsPrefabAsset`。前のものは `Backups/<名前> (before base).prefab` にコピー（Backups 以下は一覧・IsMinePath から除外）。要確認: 同じパスへの保存で GUID が変わらずシーンの参照が保たれるか、Unpack でバリアントの変更がオーバーライドとして残るか、ブループリント ID の上書きが残るか、別バージョンを重ねたもの（直接の親が元アバターでない）は対象外にしている

- `avatar.json` は項目を増やしても消さない（ほかのツールが読むため）
- `WorkFolders.ForAvatar` は改変アバターならその Prefab のフォルダ。マテリアルは「このアバターのフォルダにあるか」で判定し、別バージョンと共有しない

### 自動保存

- Undo のグループ番号が変わってから 1.5 秒操作が止まったら `ApplyPrefabInstance`。機能・アバターの切り替え、ウィンドウを閉じる、シーンを閉じる、Play の前にも保存
- 書き込み先が `Assets/KaihenNavi` 以下でなければ何もしない（元 Prefab を守る）。複製マテリアルも `SaveAssetIfDirty`

### ②のカード（目的の言葉で並べ直し）

1. 服を着せたい（新規 `OutfitFeature`・体験版）: 服の Prefab をドロップ、またはこのアバター用らしい候補から選ぶ → アバターの中に置き、MA の Setup Outfit をメニュー経由（`GameObject/ModularAvatar/Setup Outfit` など）で呼ぶ。着ている服の一覧と「脱がす／着せる」（消さずに表示切り替え）、「脱ぎ着の設定へ」
2. アクセをつけたい（`TransformFeature` を作り替え・体験版）: アクセを選ぶ → 付ける場所（頭・首・胸・腰・手首・足首）を選ぶ → 骨に付ける（アバター用に作られた位置を保ち、離れすぎていれば場所へ寄せる）→ 矢印と大きさで微調整
3. 髪や服の色を変えたい／4. 脱ぎ着できるようにしたい／以下製品版
- 候補探し（`ItemPicker`）: Prefab のパスに元アバター名の最初の語（例: Cazalis）が入っているものを候補にする。体の骨（Hips）を含むものは服、それ以外はアクセ

### 確認すること

- 新規作成で元 Prefab が無変更か（md5）、フォルダと avatar.json ができるか、自動保存で変更が Prefab に入るか
- ベース: `SaveAsPrefabAsset(インスタンス)` でバリアントになるか、2 体目で同じベースが再利用されるか、ベースを変えたら改変アバターに反映されるか
- 服: Setup Outfit のメニュー名が MA 1.18.7 で合っているか、着せた服がシーンとアップロードで正しいか
- アクセ: 猫耳などを頭に付けた位置、手首の向き
- 別バージョン（製品版）の色変えが元に影響しないか、名前変更、VRChat SDK のビルドが通るか

## 4.6 将来の構想（ユーザーの希望、2026-10-19）

- **ファイル構造の管理**: 初心者の Assets 直下は BOOTH の商品フォルダが散らばりがち。ただし買ったアセットのフォルダは**動かさない**方針（中のスクリプトがパスを決め打ちしていたり、商品を更新すると元の場所に入り直して二重になったりする）。ツールが作るものだけを `Assets/KaihenNavi` 以下にきれいに置き、買ったものは「台帳」で整理して見せる
- **アイテムのインポートと検索**: unitypackage の取り込みをツール経由にし、`AssetDatabase.importPackageStarted/Completed` と `AssetPostprocessor` で「どの商品からどのファイルが入ったか」を台帳（JSON）に記録する。種類（衣装・髪・アクセ・ギミック・ツール）や対応アバターで検索し、Prefab をそのまま着せられるようにする
- **サムネイルとの対応付け／KonoAsset 連携**: 商品のサムネイル画像を台帳に持たせ、Unity 内で見た目から探せるようにする。KonoAsset（アセット管理アプリ）のデータと連携できるかは要調査。将来の希望
- **BOOTH Library Manager（BLM）連携**: BLM でダウンロード済みの商品を Unity 内で検索・一括インポートする拡張機能が公開予定という投稿あり（商品のサムネイル・バージョン・導入状況を一覧表示）。BLM が持つ商品情報（サムネイル・バージョン）を台帳の情報源にすれば、「アイテムとサムネの対応付け」「インポート台帳」に近道できるかもしれない。要調査
- **インナーカラーを 1 クリックで**: 髪の内側だけ色を変える。内側用のメッシュやマテリアル枠が分かれているアバターはそこへ色を当て、分かれていなければ頂点の位置（頭の中心からの距離・下側の層）からマスクを作って lilToon の 2nd/3rd カラー＋マスクで塗る案。アバターごとの差が大きいので要調査
- **アニメーションコントローラーの差し替え（歩き方など）を想定する**: 改変ではアバターの Base（歩き方）や Sitting / Action のコントローラーを差し替えることがよくある（歩き方セット、GoGo Loco など）。想定しておくこと:
  - **差し替え方は 2 通り**: (1) アバターの設定（VRCAvatarDescriptor.baseAnimationLayers）の参照を差し替える。改変アバター（バリアント）側の上書きになるだけで、元アバターは変わらない。(2) 配布物が MA の Merge Animator 入り Prefab なら、アクセと同じく中に入れるだけ。どちらで来ても扱えるようにする
  - **差し替える場所はベース（10-19 決定）**: 元アバター → ベース → 改変アバターの 2 段にしたので、歩き方はベースで差し替えれば同じ元アバターの改変アバター全員に効く。1 体だけ変えたいときは改変アバター側で差し替える（「みんな」か「このアバターだけ」を選ばせる）
  - **コントローラーそのものは書き換えない**: 中身をいじる必要が出たら、`Avatars/<名前>/Animations/` に複製してから変える（マテリアルと同じ考え方）。フォルダはそのとき初めて作る
  - **元に戻す**: いまのバックアップは baseAnimationLayers を記録していない。差し替えを扱うなら、BackupService の参加者に「アニメーションの層」を足して、どのコントローラーだったかを戻せるようにする
  - **ほかの機能との関係**: 脱ぎ着（MA ObjectToggle）や色変えは FX 側／マテリアルなので影響しない。メニュー整理（4.7）は FX を触るので、Base/Action の差し替えと混ぜない。体型・身長を変えても歩き方は Humanoid のままなので基本は問題ないが、要確認
  - **カード案**: 「歩き方を変えたい」。コントローラーか Prefab をドロップ → 種類を見分けて (1)/(2) で差し替え、「元の歩き方に戻す」ボタン。avatar.json に何を差し替えたかを残すと、ほかのツールからも分かる
  - 要確認: MA の Merge Animator で Base 層を「置き換え」できるか（バージョンによる）、GoGo Loco の現在の配布形態

## 4.7 製品版カードの方針（2026-10-19 ユーザーと決定）

- **体型・身長を変えたい**（「体型」と「身長」を 1 枚に統合）: アバターの正面シルエットを 2D の輪郭画像で出し、その上に簡単なボーン構造（Unity の Humanoid リグ設定画面のような図）を重ねる。ボーンの点をつまんで、腕・脚・胴・頭などの大きさを直感的に変える。全身の大きさ＝身長。変更は MA Scale Adjuster など非破壊で行い、視点（View Position）も自動で追従させる
- **視点の高さを直したい**: 採用。操作量は少ないが分かりにくい所なので、目の位置に自動で合わせる＋微調整
- **ピンク色を直したい**: 難しい。マテリアルの参照切れの原因判定は難しいので、まずは「シェーダーが見つからない」ものだけを対象にし、.mat に残っているシェーダーの GUID から「どのシェーダー（lilToon / Poiyomi など）を入れればよいか」を案内する範囲から始める案
- **明るさや影を整えたい**: 候補は (a) lilToon の影や明るさの項目名（「影1」など）を分かりやすい言葉に置き換えた UI（破壊的でよければ）、(b) ユーザー作の非破壊マテリアル修正ツール（com.suzuki-y0.jeni.liltoon-material-override）を取り込む、(c) ゲーム内で明るさを変えたいなら、ユーザーが挙げた無料ツール（lilycalInventory と思われる、要確認）の導入を案内する。→ 10-19 決定: まずは「各アイテムのマテリアル設定を簡単にいじれる」だけで価値がある、とのことで (a) の簡易版を実装（`LightFeature`）
- **きれいなサムネイルにしたい**: 採用。アップロード用カメラの画角の初期値（60 前後）だと顔がゆがんで見えるので、狭い画角で顔に合わせる
- **アップロードとブループリント ID**（新規）: 改変アバターを作ると元の Prefab のブループリント ID を引き継ぐことがあり、別のアバターを上書きアップロードしてしまう。「新しいアバターとしてアップロード／前にアップロードしたものを更新」を自然に選べるようにする
- **元アバターの多すぎるメニューを整理したい**（新規・難しい）: MA でメニューを取り出して（Extract）管理できるようにしたい。ただし MA のメニュー項目を外しても消えるのはボタンだけで、元アバターの FX レイヤーとパラメータ（初期値・同期ビット）は残る。項目を外すとその機能は初期値のまま固定される。なので「見た目が変わらない」「ビットが減らない」ことがある。メニューの並べ替え・まとめはできるが、機能を本当に消すには FX レイヤーとパラメータまで扱う必要がある

## 4.8 製品版の実装状況（2026-10-19・主な動きは 4.4 で確認）

| カード | クラス | 中身 | Unity で確かめること |
| --- | --- | --- | --- |
| 体型・身長を変えたい | `BodyFeature` | 正面シルエット（一時カメラで撮って影絵化）＋ボーン図（Painter2D）。点をクリックで部位選択、ドラッグで上下＝長さ・左右＝太さ。部位は MA Scale Adjuster（`Scale`）、身長は Armature（Hips の親）の大きさ＋視点の自動追従。`BodyScaleBackup` で「元に戻す」に参加 | `ModularAvatarScaleAdjuster.Scale` の名前、シルエットが出るか（lilToon がアルファを書かない場合は色で判定）、点の位置ずれ、長さ方向の軸の判定、身長変更後の足の位置 |
| 視点の高さを直したい | `ViewFeature` | 目のボーン中点＋身長の 1% 前を推奨値に。矢印で上下・前後。シーンにピンクの玉 | View Position の座標系（`TransformPoint` で合っているか）、玉の表示 |
| ピンク色を直したい | `PinkFeature` | `Hidden/InternalErrorShader`・`Hidden/Locked/`・非対応・エラーを検出。.mat の中のプロパティ名（`_lilToonVersion` など）からシェーダーを判定して入手先を案内。空のマテリアル枠も一覧 | Poiyomi・UTS・Sunao の判定用プロパティ名が正しいか（要検証） |
| きれいなサムネイルにしたい | `ThumbnailFeature` | 画角 18° の一時カメラで顔／上半身／全身・正面／斜め・背景色を選んで撮影。1200×900 の `Thumbnail.png` をアバターのフォルダに保存し、SDK で選んでもらう | 撮影結果の見た目、lilToon の描画、SDK でその画像を選べるか |
| 服からはみ出る肌を隠したい | `SkinFeature` | (1) 素体のシュリンク用シェイプキー（名前に shrink / hide / 縮 / 隠 などを含むもの。「そのほかも出す」で全部）。「いつも隠す」は値を直接、服を選ぶと服の下の `KaihenNavi_SkinHide` に MA Shape Changer（Set 100）を置き、その服を着ているときだけ縮む。(2) MA に Mesh Cutter があれば（リフレクションで型を探す）服の下に置いて選択し、範囲は MA の画面で設定してもらう | `ModularAvatarShapeChanger.Shapes` / `ChangedShape` / `ShapeChangeType` の名前、Mesh Cutter の型名 `nadena.dev.modular_avatar.core.ModularAvatarMeshCutter`（要確認） |
| 明るさや影を整えたい | `LightFeature` | lilToon のマテリアルだけを、アバター直下のアイテム（素体・髪・服など）ごとに一覧。チェックした行に、おすすめ（暗い場所でも見やすく / 影をやわらかく / まぶしくならないように / lilToon の標準）か、5 本のスライダー（`_LightMinLimit` `_LightMaxLimit` `_AsUnlit` `_ShadowStrength` `_ShadowBlur`、画面では ×100）を適用。変更前に MaterialCopier で複製。`_UseShadow` がオフのマテリアルは数を案内 | lilToon の標準値（MinLimit 0.05 / MaxLimit 1 / AsUnlit 0 / ShadowStrength 1 / ShadowBlur 0.1 と記憶で書いた）、プロパティ名、lilToon 判定 `_lilToonVersion` が HasProperty で取れるか、ロック（最適化）済みマテリアルで変わるか |
| アップロード先（体験版・①に表示） | `MyAvatar.BlueprintId` など | 新規作成と別バージョンでブループリント ID を外す。①に「まだアップロードしていない／前のアバターを更新」と「新しいアバターとして出す」。ほかの改変アバターと同じ ID なら警告 | `VRC.Core.PipelineManager.blueprintId` の扱い、SDK のアップロードが新規になるか |

未着手: なし（メニュー整理は 4.4 の後の節を参照）

## 4.9 アップロード前のチェックと「今すぐ保存」（2026-10-21・Unity 確認済み）

- 経緯: 猫耳アクセ（`_Project/Items/Accessories/Ears_Tails/441/cat/fbx/*.fbx`）で、SDK が英語の警告（Read/Write オフ、Blendshape Normals が Calculate なのに Legacy オフ）を出してアップロードを止めた。初心者が詰まりやすいので先に日本語で知らせる
- `Core/UploadCheck.cs`: アバター内の SkinnedMeshRenderer / MeshFilter のメッシュから ModelImporter を引き、`isReadable == false` と「シェイプキーあり かつ `importBlendShapeNormals == Calculate` かつ `legacyComputeAllNormalsFromSmoothingGroupsWhenMeshHasBlendShapes == false`」を見つける（SDK と同じ判定のつもり。要確認）
- 直し方（ユーザーと決定）: 確認ダイアログで「モデルの形や色は変わらない／SDK の Auto Fix と同じ／配布ファイルの .meta を変えるので元に戻すでは戻らない」と説明してから、`isReadable = true` と legacy を true にして `SaveAndReimport`。元アセットを書き換えないルールの例外として、ユーザーが了承済み
- 表示: ①の下（`kn-upload-check`）。アバター選択時と hierarchyChanged（アクセ・服を足したとき）に見直す。体験版でも使える
- 「今すぐ保存」: 自動保存の帯（改変アバターのとき）に追加。`_savedGroup` を進めてから `SaveNow()`
- **判定は SDK と同じにした（10-21）**: SDK（Avatars 3.10.4）の `VRCSdkControlPanelAvatarBuilder` を読んで合わせた。対象はスキンメッシュ・メッシュ・パーティクルのメッシュ（EditorOnly の下は除く）。ModelImporter のあるメッシュだけ見る（.asset のメッシュは SDK も見ないので出さない）。法線はシェイプキーの有無に関係なく `importBlendShapeNormals == Calculate` かつ legacy オフ。legacy は SDK と同じく ModelImporter の非公開プロパティをリフレクションで読み書きする（SerializedObject では取れない）。プロパティが見つからないときは「確認できませんでした」と出して自動では直さない
- **Unity で確認（10-21、MCP Project / Cazalis 改変＋猫耳 ear.prefab）**: SDK と同じ 2 つ（Read/Write オフ・シェイプキーの法線）を ear.fbx で検出 → 「直す」→ 確認ダイアログ → 「1 個のモデルの読み込み設定を直しました」→ かいへんナビの警告も SDK の 2 つの赤いエラーも消えた。「今すぐ保存」で保存時刻が更新された。確認後、猫耳を外し、ear.fbx などの .meta を元に戻した
- 気づいた点: 確認ダイアログでは長いパスのせいで「・」だけが 1 行目に残る（Windows の折り返し）。保存の帯は手動で保存しても「自動で保存しました」と出る
- 分担の記録: FBX 以外・確認不能の扱いの追加は Codex（GPT-6.1-Sol）に任せた。そのあと SDK の実装と照らして判定を直した

## 4.10 体型：長さを変えたら子が追従する（2026-10-21・Unity 確認済み）

- 問題（ユーザー報告）: すねを長くすると、Scale Adjuster は子の骨を動かさないので、すねだけ伸びて足首が靴を突き抜け、足は元の位置のままだった。MA のインスペクターの「子オブジェクトの位置を調整」（初期値オン）は MA のハンドル操作のときだけ効き、こちらは値を直接書き換えていたので効かなかった
- 方針（ユーザーと決定）: **太さは子に何も影響させない。長さを変えたときだけ、子の位置を骨の向きに沿って動かす**（子の大きさは変えない）。頭・手・足（全体の大きさ）は目や指の付け根が合うよう全方向に動かす。長さ・太さは世界の XYZ ではなく骨の向き（次の人型の骨がある軸）で決めている
- `BoneFollow`（BodyFeature.cs）: 子の位置＝「ベースの Prefab での位置（改変アバターの Prefab より手前の対応物）× 長さの倍率」で毎回計算し直す。ずれがたまらず、100% に戻すとぴったり戻る。前の版でずれたままの子も、次に動かすと直る。ベースが無い子（あとから足したアクセなど）は倍率の比で動かす
- 脚が長くなると足が床にめり込む（SDK も「Avatar feet are beneath the avatar's origin」を出す）→ 腰（Hips）を上下させて、足首・つま先を「ベースでの足の高さ × 身長の倍率」に戻す。視点も目の高さの変化分だけずらす（視点機能での微調整は消さない）
- 「元に戻す」（BodyScaleBackup）でも子の位置と足の高さを合わせ直す
- Unity で確認: すね 145%→139% で足と靴が一緒に下がり、腰が上がって床の警告が消えた。太さ 130% では靴は動かない。「この部位をもとに戻す」で元の形に戻った
- 服: ユーザーが Unity で腕を伸ばして確認、服も一緒に伸びた（10-21）
- 未確認: VRChat 内での歩き・IK（人型のプロポーションが変わるため）
- **頭・手・足は骨ごと大きくする（10-21 変更）**: Scale Adjuster だと子の大きさが変わらず、頭だけ大きくなって髪・耳・アクセが元の大きさのまま位置だけずれる（ユーザー報告「不思議な挙動」）。手も手の甲だけ太くなり指は変わらなかった。末端の部位は Transform の localScale（ベース × 倍率）で変え、髪・指・靴・付けたアクセも一緒に大きくする。前の版の Scale Adjuster が残っていれば外す。バックアップにも頭・手・足の localScale を含めた（古いバックアップではベースの大きさに戻す）。Unity で頭 131%・手 131% → 元に戻すを確認
- **反映の速さ（10-21）**: スライダーの 1 刻みごとに全メッシュを焼いて身長を 2 回測っていた。視点は目の骨の移動量だけでずらし、身長の表示とシルエットは動かし終わって 0.15 秒後に 1 回だけ測るようにした。MA の Scale Adjuster のシーン上のプレビュー（NDMF）の遅れは MA 側なので、こちらでは変えられない

## 4.11 10-22 の追加（Codex＝GPT-6.1-Sol に実装を任せ、Claude がレビュー・Unity 確認）

- 進め方: Codex CLI（クラウドの作業環境、ChatGPT アカウントでログイン）に、タスクごとの指示書と共通ルールを渡し、git worktree で並行に実装させる。Claude が差分を読み、直し、check.sh・Unity で確認してマージ。ユーザーの指示で、Codex の 5 時間枠が上限に達したら使用をやめる（使用率は ~/.codex/sessions の rate_limits で見る）
- A 脱ぎ着の候補の分け方（ToggleFeature）: 「服・アクセ」と、折りたたみの「そのほか（髪・体など）」。素体・顔の参照と、メッシュ名（PartGuesser）で判定。名前に服の言葉があれば服を優先（Cloth_HairRibbon）。Unity 確認済み（Cazalis: Body・Body_Base・Hair_* の 10 個が「そのほか」）
- B 候補の自動更新: FeatureBase.RefreshOnHierarchyChange（服・位置と大きさ・脱ぎ着・肌）。アバター内の構成ハッシュが変わったときだけパネルを作り直す。肌の「いつ隠すか」は服そのもので覚える。Unity では未確認（アクセを足して候補が増えるか）
- C アイテム台帳（Core/ItemLedger.cs）: unitypackage の取り込み（importPackageStarted〜Completed）で入ったファイルを Assets/KaihenNavi/Ledger/items.json に記録。服・アクセの候補にも加え、「◯◯ から」と表示。実際の unitypackage 取り込みでは未確認
- E 台帳の続き: 「今入っているアイテムを読み込む」（今ある Prefab を商品フォルダごとに登録。置き場フォルダは 5 段まで掘る）、服・アクセの画面に「持っているアイテムから探す」（名前・商品名・フォルダで絞り込み）。Unity 確認済み（MCP Project で 8 グループ、「V2」で MONOLITH_Shinano_V2 が出た）
- D ピンク色の判定: Poiyomi・UTS・Sunao の判定用プロパティを公式ソースで確認して直し（根拠 URL はコード内）、当たった数が最多のシェーダーを選ぶ。実際の壊れた Poiyomi などでは未確認
- 改変アバター一覧に Thumbnail.png があれば表示（Unity 確認済み）
- 説明書 Docs/manual-ja.md（Codex が執筆）。画像の場所は <!-- 画像 --> で示してある

## 4.12 10-22 夕方の追加（Codex 実装・Claude レビューと Unity 確認）

- 文言: 英中韓を見直し、対訳表 Docs/glossary.md（G2）
- 髪の内側の色を変えたい（InnerColorFeature、製品版）: 髪メッシュ（lilToon）を選び、内側を「外から見えるか」（髪と体へ 64 方向のレイ。openness）で判定してマスク画像を作り、lilToon の 2nd カラーで色を乗せる。最初の「頭の中心からの距離」方式は長い髪が全部内側になったので作り直した。Unity 確認済み（Cazalis で外側は銀のまま、内側の層だけピンク）。プレビュー／元に戻すを繰り返してもファイルが増えないこと、元に戻すでツールの生成物（未参照のもの）が片付くことも確認。元に戻すの参照調査はツールのフォルダとシーンに限定（G1d、Unity での待ち時間は未確認）
- KonoAsset 連携（KonoAssetLink、読み取り専用）: preference.json / Documents/KonoAsset から metadata（version 3）と data/<id>/ の unitypackage 名で索引（Library/KaihenNavi にキャッシュ）。取り込んだ unitypackage 名で自動対応、今あるフォルダは浅い順に商品名と照合（Items・Prefab・Clothing などは除外）、手動の選択も可。候補にサムネイルと「商品名 ／ 作者」、対応アバターで並べ替え。Unity 確認済み（360 商品、MONOLITH・高速移動セット・カザリスが自動対応、サムネイル表示）
- 調査メモ Docs/research-asset-managers.md（KonoAsset・BLM）。BLM は今回は使わない
- 注意: computer use で open_application("Unity") を使うと Unity Hub が前に出てユーザーの邪魔になる。Unity のウィンドウを直接クリックする

## 4.13 服のライブラリ（2026-10-25・Unity 実機は未確認）

- 検索不具合の原因: 旧 `ItemPicker.OwnedItems` は Ledger に登録された Prefab だけを検索しており、`KonoAssetLink.All` の全商品索引を使っていなかった。未取り込みの商品は必ず対象外になり、大小文字以外の表記揺れも吸収していなかった。
- `Core/ItemLibrary.cs` に服・アクセ共通の「商品一覧 → 行内の詳細 → 確定」を実装。商品名・ショップ名・フォルダ名・Prefab 名・unitypackage 名で部分一致。NFKC、大小文字、ひらがな／カタカナを正規化する。検索対象は全件、表示だけ24件ずつに分ける。「このアバター用」が初期値、対応情報と Prefab パスで絞り込む。
- KonoAsset と Ledger、および既存 Prefab の読み取り専用の探索結果を商品単位で結合。一覧更新・装着では台帳に書き込まない。外部データも変更しない。商品索引は出所の JSON 名を追加してキャッシュ形式を2にした。
- 詳細に画像、商品・ショップ、Prefab の選択、Project の「場所」、確定ボタン。アクセは確定後に従来の付け場所選択へ進む。`TransformFeature` は共通一覧の呼び出しだけ変更し、付ける処理・位置調整部分は変更していない。
- 「ファイルから選ぶ…」は「Project から選ぶ…」として補助欄に残した。OS のファイル選択との混同を避け、ドラッグが難しい人も使えるようにするため。「読み込み直す」は1つ、KonoAsset のデータ場所は「設定」にまとめた。他機能へのボタンと説明は左ナビの翻訳を参照する。
- 未取り込みの商品は `.unitypackage` を選択して `ImportPackage(..., true)`。選択した商品IDと検索・絞り込みを SessionState に保持し、取り込み後も詳細を復元する。
- 取り込みのファイル Undo は `PackageImportUndo` / `PackageImportState`。gzip/tar の pathname・内容ハッシュ・GUID で既存ファイルへの上書きや別の場所にある同一 GUID を事前に拒否。追加ファイルと .meta、および台帳・ツールの README の前後を `Library/KaihenNavi/Imports/` に保存し、Undo / Redo で復元する。既存の配布ファイルは書き戻さない。版の違う既存ファイルを含むパッケージは、安全のため取り込み全体を停止する。
- 装着はインスタンス生成・MA の処理・バックアップへの追加パスの記録・自分用 Prefab への保存まで同じ Undo グループにまとめる。`MyAvatar.Save` の Apply を UserAction に変更し、保存の取り消しも記録する。服の操作内で保存することで、後の自動保存が装着の Undo を分けないようにした。
- `bash tools/check.sh`: NoMA / WithMA / Full のコンパイル、4言語543キー、KonoAsset の60検証が成功。検索・結合、アーカイブ読取、衝突拒否、追加ファイルと GUID・台帳の Undo/Redo 用ファイル復元を検証。Unity の JsonUtility・実 Undo・MA 実行はスタブでは確認できない。
- **Unity で確認すること**: 行の選択だけでは服が増えないこと／検索と「すべて」の切り替え／画像と狭い幅・両スキン・4言語／装着後に1.5秒待つ・ページ切り替え・保存・ウィンドウ再起動を挟んだ Ctrl+Z と Redo（MA の部品も戻ること）／取り込みの完了・キャンセル・失敗・スクリプトによるドメインリロード後の詳細復元／取り込み Undo で新規ファイル・.meta・台帳が一緒に戻り、Redo で GUID が保たれること／元アセットのハッシュが不変なこと。
## 4.13 アクセ・脱ぎ着と Ctrl+Z（2026-10-25・Unity 実機は未確認）

### 原因と修正

- `MyAvatar.Save` が `ApplyPrefabInstance(..., AutomatedAction)` を使っていた。シーンの追加物が Prefab に適用される一方、その適用自体は Undo に記録されないため、装着時／メニュー作成時のシーンの Undo だけでは保存先も含めた状態を戻せなかった。`Undo.ClearAll` などで履歴を明示的に消す処理は対象経路にはなかった。
- アクセの装着では最初の親変更に通常の `SetParent` を使い、最終位置の直接代入の記録とグループの確定も不足。メニュー削除には `CollapseUndoOperations` が無かった。作成は既にグループ化されていたが、後から行う保存がそのグループの外だった。
- 対象機能は変更 → `FlushUndoRecordObjects` → `UserAction` で自分用 Prefab に保存 → 元の編集グループに Collapse、までを操作内で行う。アクセのスライダーはドラッグ終了／数値欄のフォーカス終了までを1手として、その間のタイマー保存を待たせる。Scene の移動などは `postprocessModifications` で記録した編集グループに保存をまとめる。
- `KaihenNaviWindow.OnUndoRedo` は画面の再構築だけだった。Undo 後のグループ番号変化を自動保存が新しい変更と誤認すると、再保存が Redo を消すため、新しい編集の記録または明示的な保存まで自動保存を待たせる。再構築自体はモデルを変更しない。未確定のスライダー保存も Undo 時に破棄する。
- `BackupService.BeginEdit`／`NoteCreated` はバックアップ用アセットの保存であり、アバターへの Prefab 適用は行っていなかった。Undo 後はバックアップの操作区切りを終了する。`InnerColorAssetUndo` の復元コールバックだけは、新しい Undo を作らないよう `Save(avatar, false)` を使う。
- 根拠: [Unity 2022.3 の ApplyPrefabInstance 実装](https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3/Editor/Mono/Prefabs/PrefabUtility.cs)、[UserAction の説明](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/InteractionMode.UserAction.html)。実機の Undo／Prefab インポートはスタブでは再現できないため、下記の確認を必ず行う。

### アクセの変更

- 「つけたアクセ」一覧に「調整」「外す」を追加。以前の「どれを直しますか？」の欄と矢印は同じ調整パネルに統合した。装着直後は調整パネルをページ先頭に表示する。
- 骨の localPosition を親のスケール込みの cm で表示（Y＝上下、Z＝前後、X＝左右）。通常の範囲は身長の ±25%、既に範囲外にある値はその周囲まで広げる。骨の軸はアバターによって向きが違う。回転は localEulerAngles の3軸、大きさは推定時の localScale に対する一様な倍率（10〜300%）。
- 初期位置の判断: 元アバター名の先頭語（3文字以上）がアクセの Prefab 名／フォルダパスに入る、またはアクセの Renderer.bounds の中心が骨から身長の20%以内なら、足元基準で置いたワールド位置を保って骨の子にする。それ以外は見た目の中心を骨へ寄せ、頭なら身長の6%上、首なら2%前へ補正する。Renderer が無い場合はアクセの原点を寄せる。
- 推定値は `KaihenNavi_AttachBaseline`（EditorOnly の空オブジェクト）の Transform に記録。独自コンポーネントを残さず、Prefab 保存・再読み込み後も「推定した位置に戻す」が使える。以前からあるアクセでは最初に調整欄を開いた値を基準にする。
- 「シーンで動かす」は Selection、`SceneView.Frame(bounds, false)`、`Tools.current = Move`。外すときはアクセ全体を `Undo.DestroyObjectImmediate`。アバター自身や体の骨を含むものは削除しない。

### 服が消えた理由と変更

- 旧 `CreateMenu` は対象をすべて `SetActive(false)` にしており、MA のプレビューで見えていても実際の `activeSelf` は false だった。旧 `RemoveMenu` は作成前の状態の代わりに `Menu Item.isDefault` を使っていたため、初期値を変更した場合などに作成前へ戻せなかった。保存により旧メニューが復活／削除が完全に戻らない問題も重なり得る。作者の実際の操作順と MA の競合状態は、この環境では再現できていない。
- MA 1.18.7 の `FindObjectToggles` は対象の `activeSelf` を制御が無い側の状態として使い、`BuildConditions` は `Menu Item.isDefault` に基づく条件を別に扱う。オンの対象に Active=true を置くだけではオフにできない。そのため、メニュー親に常時 OFF の MA Object Toggle を置き、子の項目が ON のときだけ後から ON で上書きする。服本体を非表示にする処理は削除した。
- 作成前の `activeSelf` は、項目内の `KaihenNavi_OriginalVisibility`（EditorOnly の空オブジェクト）の `activeSelf` に保存。作り直しでは最初の状態とメニュー初期値を引き継ぎ、残す対象の現在の表示は変えない。チェックを外した対象とメニュー削除時は、最初の表示状態に戻す。バックアップの MenuData にも元の状態を含める。
- 古いメニュー／古いバックアップには元の表示状態が無いため、互換処理では従来の `isDefault` を使う。過去に既定値を変更していた場合の本当の作成前状態は復元できない。
- 削除ボタンは「脱ぎ着の切り替えを消す（元に戻す）」。対象が全て失われていても削除可能。4言語の文言と説明書も更新。
- 根拠: [MA 1.18.7 の対象・条件の読み取り](https://github.com/bdunderscore/modular-avatar/blob/1.18.7/Editor/ReactiveObjects/AnimationGeneration/ReactiveObjectAnalyzer.LocateReactions.cs)、[初期状態の解決](https://github.com/bdunderscore/modular-avatar/blob/1.18.7/Editor/ReactiveObjects/AnimationGeneration/ReactiveObjectAnalyzer.cs)、[Object Toggle の優先順位](https://modular-avatar.nadena.dev/ja/docs/reference/reaction/object-toggle)。

### 検証と Unity での確認手順

- `tools/check.sh`: NoMA / WithMA / Full のコンパイル、4言語535キー、既存の34項目、新しい29項目の回帰チェックが通過。新しいチェックは作成・作り直し・削除での状態復元、明示的な OFF 設定、旧データ、対象消失、保存先制限、Undo API の選択・グループ指定、スライダー終了時の保存・Undo時の中止を確認する。**Unity の Undo の実処理、Prefab の再インポート、MA のプレビュー／ビルドの検証ではない**。
- Unity 2022.3: 改変アバターと通常のシーン上アバターで、アクセ装着 → 数秒待つ → Ctrl+Z 1回 → Redo、外す → Undo、再起動後の一覧／推定値復元を確認。元 Prefab のハッシュも比較する。
- アクセ: 原点中心の小物／足元基準で作られたアバター用アクセ、移動・回転・拡縮済みのアバター、頭・首・手首で初期位置を見る。位置3軸・回転3軸・大きさのドラッグと直接入力、Scene の Move、推定位置へ戻すをそれぞれ1回の Undo／Redoで確認。
- 脱ぎ着: activeSelf が true／false の対象を混ぜ、作成 → 作り直し（名前変更・対象追加／除外）→ 削除の各段階で Undo／Redo。MA 初期値だけを手動変更しても削除時に最初の状態へ戻るか確認。自動保存待ち・「今すぐ保存」・ページ切り替え・ウィンドウ開閉のあとにも繰り返す。
- Gesture Manager と NDMF ビルドで ON/OFF の両側と初期値、対象同士に親子関係がある場合、別の MA Object Toggle と対象が重なる場合の見た目を確認。EditorOnly の目印がビルド結果に残らないことも確認する。

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

### UI の方針（2026-10-25）

- **VCC / ALCOM 風、差し色は Unity の青**。Unity のダーク／ライトスキンに合わせたグレーの面、薄い枠線、角丸 4px（カード 6px）を使う。番号の丸・色付き漢字アイコン・ピンクの装飾は使わない。アバターの色見本や撮影背景の色選択は機能の内容として残す。
- `kn-current` / `kn-status-chip`: 上部のアバター帯と小さな状態表示。`kn-upload-check` は直下の1行警告（詳細はホバー、修正前の確認ダイアログは維持）。
- `kn-workspace` / `kn-navigation` / `kn-nav-item`: 左ナビ＋右ページ。`FeatureBase.Group` と `nav.group.*` で4グループに分け、線画アイコンを添える。`Icon` / `Accent` は画面で参照しない。体験版の製品版項目にはグレーの「製品版」を表示。
- `kn-page-header` / `kn-panel`: タイトル・説明・更新と、既存の `Feature.Build`。ナビと本文は別々にスクロール。幅 560px 未満は `GeometryChangedEvent` で `kn-compact-window` に切り替え、ナビの代わりに `PopupField` を表示する。
- `kn-chooser` / `kn-choice`: アバター未選択時の中央の二択。`kn-footer`: 自動バックアップ件数・履歴・控えめな元に戻すボタン。
- `kn-step-header` は番号なし見出し、`kn-notice` は標準アイコン＋薄い枠、`kn-chip` はセグメント、`kn-list__row` は行形式。各機能の編集処理は維持し、体型図の選択点と視点マーカーも青にそろえた。
- Unity 実機では両スキン・4言語・幅 360 / 559 / 560 / 800px で、折り返し・スクロール・ナビ／ドロップダウンの選択同期・保存／履歴／警告を確認する。

## 6. ファイル構成

```
Packages/com.suzuki-y0.jeni.kaihen-navi/
  package.json / CHANGELOG.md
  Editor/
    Jeni.KaihenNavi.Editor.asmdef
    Core/  KaihenNaviWindow（画面）, BackupService / BackupSnapshot（バックアップ）,
           MaterialCopier, PartGuesser（髪・服・目・肌の判定）, AvatarFinder, HierarchyPath,
           WorkFolders, UndoHelper, L10n, Ui, PackagePaths, Edition
    Features/  FeatureBase, FeatureCatalog（ナビの並び）, ColorFeature, ToggleFeature, TransformFeature
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
