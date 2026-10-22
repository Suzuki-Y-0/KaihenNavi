# かいへんナビ 対訳表

対象は `Packages/com.suzuki-y0.jeni.kaihen-navi/Editor/Localization/` の4言語。日本語を意味の基準にする。ボタンは短い動詞、説明文は初心者が次の操作を選べる言い方にする。説明中でカードやボタンを引用するときは、その言語の表示名とそろえる。

| 日本語 | English | 中文（简体） | 한국어 |
| --- | --- | --- | --- |
| かいへんナビ | Kaihen Navi | 改装导航 | 카이헨 내비 |
| アバター | avatar | 模型 | 아바타 |
| 改変アバター | edited avatar | 改装模型 | 개조 아바타 |
| 改変のベース | shared avatar base | 改装基底 | 개조 베이스 |
| 元のアバター | original avatar | 原始模型 | 원본 아바타 |
| 改変・変更内容 | edits | 修改 | 변경 내용 |
| 服 | outfit | 衣服 | 옷 |
| アクセ | accessory | 饰品 | 액세서리 |
| 脱ぎ着 | outfit toggles | 穿脱 | 입고 벗기 |
| 脱ぎ着メニュー | outfit toggle menu | 穿脱菜单 | 입고 벗기 메뉴 |
| 元に戻す（バックアップから） | Restore | 恢复原状 | 되돌리기 |
| 元に戻す（個別の設定） | Restore … | 恢复… | …되돌리기 |
| 製品版 | Full version | 完整版 | 정식판 |
| 体験版 | Trial | 体验版 | 체험판 |
| Expression メニュー | expression menu | 表情菜单 | 익스프레션 메뉴 |
| メニューの項目 | menu item | 菜单项 | 메뉴 항목 |
| オン・オフ | on / off | 开关 | 켜기·끄기 |
| 素体・体のメッシュ | body mesh | 素体（身体网格） | 소체（몸 메시） |
| シェイプキー | shape key | 形态键 | 셰이프 키 |
| マテリアル | material | 材质 | 머티리얼 |
| シェーダー | shader | 着色器 | 셰이더 |
| 視点 | viewpoint | 视点 | 시점 |
| 動き | animation | 动作 | 동작 |
| エモート | emote | 表情动作 | 이모트 |
| サムネイル | thumbnail | 缩略图 | 썸네일 |
| 自動バックアップ | automatic backup | 自动备份 | 자동 백업 |
| 履歴 | History | 历史 | 기록 |
| アップロード | upload | 上传 | 업로드 |
| アイテム | item | 道具 | 아이템 |
| Modular Avatar | Modular Avatar | Modular Avatar | Modular Avatar |
| VRChat SDK | VRChat SDK | VRChat SDK | VRChat SDK |
| Mesh Cutter | Mesh Cutter | Mesh Cutter | Mesh Cutter |

## 用語の使い分け

- 「元のアバター」は購入・導入した元ファイル。「改変のベース」はツールが作る共有の土台。「改変アバター」はその土台から作る編集対象。この3つを区別する。英語の `base`、韓国語の `베이스` は、同じ説明内で共有の土台を示したあとの短縮形として使う。中国語では物理的な台座を連想させる「底座」を使わない。
- 「脱ぎ着」は服・アクセの表示切り替え。メニュー名は英語 `Outfit toggles`、中国語「穿脱」、韓国語「입고 벗기」。服を追加する「着せる」と区別する。
- `Restore` はバックアップや元の設定へ戻す操作。`Ctrl+Z` はその操作自体を取り消す場合もあるため、両者を混同しない。「恢复原状」「되돌리기」も同様。
- `shape key` / 「形态键」 / 「셰이프 키」は Unity の BlendShape に相当する。Unity の実際のプロパティ名やファイル名を案内するときは、`Read/Write`、`Auto Fix`、`Unlock`、`Lock`、`Prefab`、`Project`、`Inspector`、`Console`、`Thumbnail.png` などをそのまま残す。
- 韓国語のカード見出しは「…싶어요」、説明文は「…합니다／…해 주세요」、ボタンは「…하기」を基本にする。英語の大文字・複数形、各言語の助詞や語尾は文脈に合わせる。

## 参考にした表記

訳語はこのUIのために選定したもので、すべてが各サービスの公式訳というわけではない。

- 英語の `outfit` と、メニュー・表示切り替えの説明は [Modular Avatar の公式ガイド](https://modular-avatar.nadena.dev/docs/intro)を参照。
- 中国語の「模型」「形态键」「表情菜单」は [VRChat 汉化文档中心のアバター解説](https://docs.vrczh.org/creators.vrchat.com/avatars/)と[メニュー解説](https://docs.vrczh.org/docs.vrchat.com/docs/action-menu)を参照。メニュー全体を表すため、表情だけに限定する説明は避ける。
- 韓国語の「아바타」、衣装を着せる操作の言い回しは [Kisetter の韓国語ガイド](https://kisetter.edenlabs.workers.dev/ko/docs/auto-fitting/)を参照。衣装の呼び方は初心者向けの「옷」に統一する。

## 整合性の確認

2026-10-22 の見直しでは、変更前・変更後とも各言語424キー。重複キーがなく、全キーの集合・順序、差し込みの番号と出現回数、改行数を日本語と照合した。差し込みは集合だけでなく出現回数も一致させる。`walk.confirmResetAll` は日本語が `{1}` のみを使うため、そのまま維持する。

日本語の表記修正は `transform.selectionInvalid` と `transform.none` の「アクセサリー→アクセ」、`body.resetPart`・`body.resetAll`・`body.resetAllConfirm` の「もとに戻す→元に戻す」の5件。

Unity では各言語に切り替え、カード・ボタン・確認ダイアログの折り返しと文字切れ、生成される脱ぎ着メニューの表示名を確認する。
