# VR Stick Scope

VR Stick Scope は、Quest コントローラーのスティック入力を確認し、VRChat 向けに補正入力を試すための Windows ツールです。SteamVR と VRChat を使う環境を想定しています。

この公開版は SteamVR ドライバーをインストール・登録しません。OpenVR 経由で入力を読み取り、必要な場合だけ VRChat OSC へ補正入力を送信します。

## ダウンロード

使うだけなら、ソースコードではなく次の ZIP をダウンロードしてください。

[VR-Stick-Scope-v1.0.7-public.zip](https://github.com/Oinari-Sama/VR-STICK-SCOPE-PUBLIC/releases/download/v1.0.7-public/VR-Stick-Scope-v1.0.7-public.zip)

GitHub が自動で表示する `Source code (zip)` には起動用 exe が入っていません。上の ZIP を展開すると、直下に `00_START_VRStickScope.exe` があります。

## できること

- 左右スティックの入力をリアルタイムに表示します。
- ガイドに合わせてスティックを回し、弱い方向、中心へ落ちる方向、反対方向へ飛ぶ入力を確認します。
- 必要に応じて、VRChat の OSC 機能へ補正後の移動入力と旋回入力を送ります。

## 確認済み環境

現在確認できている環境は Quest 2 + Virtual Desktop + SteamVR + VRChat の組み合わせのみです。ほかの環境での動作報告や不具合報告を歓迎します。

## 対象環境

- Windows
- SteamVR
- SteamVR から Quest コントローラーを使える環境
- VRChat OSC 補正を使う場合は、VRChat 側の OSC を有効にしてください。

診断時は、HMDをPCと接続した状態で SteamVR と VRChat を起動し、アバターを操作可能な状態にしてください。

## すぐ使う

1. 公開 ZIP をダウンロードします。
2. ZIP を右クリックして、すべて展開します。
3. 展開したフォルダーの `00_START_VRStickScope.exe` を起動します。
4. HMDをPCと接続した状態で、SteamVR と VRChat を起動します。
5. アバターを操作可能な状態で、`入力を見る` 画面を開き左右スティック入力が反応することを確認します。
6. `回して故障診断` を開き、`計測開始` を押します。
7. オレンジのガイドに合わせて、スティックを操作します。

## 画面

- 入力を見る: スティックの現在値と動きを確認します。診断や補正作成は「回して故障診断」で行います。
- 回して故障診断: ガイドに沿って入力し、方向ごとの状態を調べます。
- 補正データ: 作成した補正プロファイルを管理します。
- 出力設定: VRChat OSC 出力と SteamVR 自動起動を管理します。

## VRChat OSC 補正

VRChat で補正を試す場合は、先に VRChat 側で OSC を有効にしてください。VRChat は起動したまま、アバターを操作可能な状態にします。その後、VR Stick Scope の `出力設定` で `VRChatへ補正入力を送る` を押します。

送信する OSC 入力は次の3つです。

```text
/input/Horizontal      左右移動
/input/Vertical        前後移動
/input/LookHorizontal  左右旋回
```

診断だけを行う場合、VRChat への補正送信は行われません。

## SteamVR 自動起動

`SteamVR自動起動を有効化` を押すと、SteamVR 起動時に VRChat OSC 補正が自動で始まります。毎回手動で開始したい場合は有効化しないでください。

不要になった場合や入力がおかしいと感じた場合は、`出力設定` で `SteamVR自動起動を解除` を押し、VR Stick Scope、SteamVR、VRChat を終了してから起動し直してください。詳しくは公開 ZIP 内の `docs\ROLLBACK_JA.txt` を参照してください。

## 注意と免責

- VR Stick Scope は VRChat、SteamVR、Meta、Valve の公式ツールではありません。
- 診断と補正処理はローカルで行われます。
- コントローラー入力データを外部サーバーへアップロードしません。
- このアプリは一時的に操作を補助する目的のツールです。物理的なスティック故障の修理を目的としたものではないため、症状がある場合は早めの修理や交換をおすすめします。
- プロファイルとローカル設定は、ユーザーのローカルアプリケーションデータ内の `VRStickScope` に保存されます。

## ライセンス

VR Stick Scope は MIT License で公開しています。詳細は `LICENSE` を参照してください。

配布 ZIP には OpenVR、Microsoft Windows App SDK、Win2D、WebView2、.NET Runtime などの第三者コンポーネントが含まれます。これらはそれぞれのライセンス条件に従います。詳細は ZIP 内の `docs\THIRD_PARTY_NOTICES.txt` と `docs\licenses\` を参照してください。
