あなたは Unity 2022.3 のエディタ拡張「かいへんナビ」（このリポジトリ、パッケージ Packages/com.suzuki-y0.jeni.kaihen-navi）の開発担当です。対象は VRChat 改変の初心者。日本語でコメントを書き、既存のコードスタイル（C# 7.3 相当、var、日本語 XML コメント）に合わせてください。まず HANDOFF.md と関係するコードを読んで全体を把握してください。

## 守るルール
- コードは Editor 専用。アップロードされるアバターに独自スクリプトを残さない（MA などの既存コンポーネントは可）。
- アバターや服・アクセの元アセットは書き換えない（必ず複製してから変更）。
- 新しいファイルを足したら `python3 tools/gen_meta.py` で .meta を作る。
- Unity はここでは動かせない。`bash tools/check.sh` でスタブを使ったコンパイル確認と翻訳チェックができるので、最後に必ず通すこと（スタブが足りなければ tools/stubs に最小限追加してよい）。
- 言語ファイル（Editor/Localization/*.json）に新しいキーを足すときは ja / en / zh-Hans / ko すべてに入れる。文言は初心者が読んで分かる言葉で。画面上の同じ機能の名前は、左ナビの名前（feature.ID.nav）とボタン名などでそろえる。
- UI は Editor/UI/KaihenNavi.uss の既存トークンとクラス（kn-*）を使い、今の VCC 風デザインに合わせる。Unity 標準の GenericMenu のような見た目のものを新しく足さない。
- **このツールで行う変更はすべて Ctrl+Z（Undo）で戻せること。** Undo.RegisterCreatedObjectUndo / Undo.RecordObject / Undo.AddComponent / Undo.DestroyObjectImmediate / Undo.SetTransformParent などを使い、1 つの操作は Undo.SetCurrentGroupName + CollapseUndoOperations で 1 手にまとめる。ウィンドウ側の自動保存やバックアップが Undo 履歴を壊さないか注意。
- git commit はしないこと（こちらでレビューしてコミットする）。最後に変更点と、Unity で確認が必要な点を日本語で短くまとめる。
## 担当：「体型・身長を変えたい」のちらつき（Features/BodyFeature.cs）と、履歴の表示（Core/KaihenNaviWindow.cs、Core/BackupService.cs、Core/BackupSnapshot.cs）

ユーザー（作者）が Unity で触った感想：
- 体型：影絵の点（骨）をクリックして選ぶたびに、画面全体（左ナビのグループ名「服とアクセ」「色と見た目」や、ページの見出し「体型・身長を変えたい」など、他の UI も全部）が描き直されてちらつく。変わらない所は描き直さないでほしい。
- 履歴：右上の履歴ボタンを押すと、謎の「10」という項目が出て、そこからさらに小さいメニューでこれまでの操作が見える。「10」はいらない。Unity 標準のメニューの見た目（GenericMenu）なのも嫌。
- 履歴：今は「服を着せる」「体型・身長を変える」など機能ごとにひとまとめになっている。それはそれで良いが、その中をさらに細かい手順で見られるようにしたい（例：直近で服を着せるをずっといじっていたら、「服を着せる」を開くとその中の細かい履歴が並ぶ）。

## やること
1. 体型：点の選択・部位ボタン・スライダー操作で、ウィンドウ全体の RebuildAll / BuildNavigation / BuildPanel を呼ばないようにする。ページ内の必要な部分（選択中の部位名、スライダーの値、影絵の再描画 MarkDirtyRepaint）だけ更新する。原因（ctx.Rebuild を呼んでいる、ヒエラルキー変更イベントで全体を作り直している、FontLoader の太字差し替えの再適用、など）を特定してまとめに書く。ヒエラルキー変化による QueueScan → 全体再構築も、ナビのバッジなど必要な所だけ更新するよう見直す（他の機能でも同じちらつきが起きないように）。
2. 履歴を独自の UI にする：
   - 履歴ボタンで、ウィンドウ内にポップオーバー（UI Toolkit の VisualElement で作ったパネル。PopupWindow / EditorWindow.ShowAsDropDown を使ってもよいが、中身は UI Toolkit と KaihenNavi.uss で作り、今のデザインに合わせる）を出す。
   - 1 段目：作業のまとまり（機能名＋時刻＋件数）を新しい順に。2 段目：まとまりを開くと、その中の細かい手順（Undo グループ名、時刻）を新しい順に。各手順に「ここまで戻す」。
   - 謎の「10」が何だったのか（件数？深さ？）を調べてまとめに書き、それを出さない。
   - 細かい手順は、各機能が Undo.SetCurrentGroupName で付けている名前を使う。名前が無い/英語の手順は分かりやすい日本語にする（L10n）。どの機能の手順かは、操作したときに選んでいた機能 Id で分類する。
   - 「ここまで戻す」は既存のバックアップ/復元の仕組み（BackupService）と矛盾しないようにする。Undo 履歴で戻せるものは Undo で、それ以前のものはバックアップから、など既存の設計を読んで決め、まとめに書く。
   - 元に戻すアイコンボタン（↶）は今まで通り直前の 1 手を戻す。
