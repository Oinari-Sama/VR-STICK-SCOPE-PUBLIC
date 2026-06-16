# VR Stick Scope 使い方

VR Stick Scope は、Quest コントローラーのスティック入力を確認し、VRChat 向けに補正を試すためのツールです。SteamVR の追加ドライバーなしで使えます。

## まず起動する

1. ZIP を右クリックして、すべて展開します。
2. 展開したフォルダの `00_START_VRStickScope.exe` を起動します。
3. `Runtime` を開きます。
4. まずは `診断だけ開始` を押します。

## スティックを調べる

`回して故障診断` を開き、オレンジの目標点に合わせてスティックをゆっくり回してください。方向ごとの入力の強さや、入力が飛びやすい場所を確認できます。

`入力を見る` では、今のスティック入力をそのまま確認できます。細かい診断をしたいときは `回して故障診断` を使ってください。

## VRChat OSC 補正を試す

1. VRChat 側で OSC を有効にします。
2. VR Stick Scope の `Runtime` を開きます。
3. `VRChat OSC 開始` を押します。
4. VRChat 上で移動入力と左右旋回を確認します。

現在送るOSC入力は次の3つです。

```text
/input/Horizontal      左右移動
/input/Vertical        前後移動
/input/LookHorizontal  左右旋回
```

診断だけを行う場合、VRChat への補正送信は行われません。

## SteamVR 自動起動

`Runtime` の自動開始ボタンを使うと、SteamVR 起動時に VR Stick Scope のエンジンを一緒に起動できます。不要になった場合は、同じ画面から自動起動を無効化できます。

これは SteamVR ドライバー登録ではありません。VRChat OSC へ補正済みの入力を送るだけです。

## 戻し方

入力がおかしいと感じた場合は、次の順番で戻してください。

1. `Runtime` で `OSC補正の自動開始を無効化` を押します。
2. VR Stick Scope を終了します。
3. SteamVR と VRChat を終了します。
4. SteamVR を起動し直します。

詳しくは公開 ZIP 内の `docs\ROLLBACK_JA.txt` を見てください。

## 注意

- 補正を使うには、VRChat 側の OSC を有効にする必要があります。
- Quest、Virtual Desktop、SteamVR の構成によって軸の見え方が違う場合があります。
- 最初は診断だけで動作を確認してください。
