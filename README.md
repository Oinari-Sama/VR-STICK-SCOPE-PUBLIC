# VR Stick Scope

Quest コントローラーのスティック入力を確認し、VRChat 向けに補正を試すためのツールです。SteamVR と VRChat を使う環境を想定しています。

この公開版は SteamVR ドライバーをインストール・登録しません。
OpenVR 経由で入力を読み取り、必要な場合だけ VRChat OSC へ補正入力を送信します。

## ダウンロード

使うだけなら、ソースコードではなく次のZIPをダウンロードしてください。

[VR-Stick-Scope-v1.0.0-public.zip](https://github.com/Oinari-Sama/VR-STICK-SCOPE-PUBLIC/releases/download/v1.0.0-public/VR-Stick-Scope-v1.0.0-public.zip)

GitHub が自動で表示する `Source code (zip)` には起動用 exe が入っていません。
上の ZIP を展開すると、直下に `00_START_VRStickScope.exe` があります。

## できること

- 左右スティックの入力をリアルタイムに表示します。
- ガイドに合わせてスティックを回し、入力が弱い方向や飛びやすい方向を確認します。
- 必要に応じて、VRChat の OSC 機能へ補正後の移動入力と左右旋回入力を送ります。

この公開版は、SteamVR の追加ドライバーなしで使えます。補正を使う場合も VRChat の OSC 機能を使うため、まずは診断だけで動作を確認できます。

## 対象環境

- Windows
- SteamVR
- Quest コントローラーを SteamVR から使う環境
- VRChat OSC 補正を使う場合は、VRChat 側の OSC を有効化してください。

## すぐ使う

1. [VR-Stick-Scope-v1.0.0-public.zip](https://github.com/Oinari-Sama/VR-STICK-SCOPE-PUBLIC/releases/download/v1.0.0-public/VR-Stick-Scope-v1.0.0-public.zip) をダウンロードします。
2. ZIP を右クリックして、すべて展開します。
3. 展開したフォルダの `00_START_VRStickScope.exe` を起動します。
4. `Runtime` で `診断だけ開始` を押します。
5. `回して故障診断` で、オレンジの目標点に合わせてスティックをゆっくり回します。

## 画面

- 入力を見る: スティックの動きをリアルタイムで見る画面です。
- 回して故障診断: ガイドに沿って入力し、方向ごとの症状を調べる画面です。
- Profiles: 補正プロファイルを管理します。
- Runtime: 診断用エンジン、VRChat OSC、SteamVR 自動起動を操作します。

## VRChat OSC 補正

VRChat で補正を試す場合は、先に VRChat 側で OSC を有効にしてください。
その後、VR Stick Scope の `Runtime` で VRChat OSC を開始します。

現在送るOSC入力は次の3つです。

```text
/input/Horizontal      左右移動
/input/Vertical        前後移動
/input/LookHorizontal  左右旋回
```

診断だけを行う場合、VRChat への補正送信は行われません。

## SteamVR 自動起動

Runtime から、SteamVR と一緒に VR Stick Scope のエンジンを起動する設定を追加できます。不要になった場合は、同じ Runtime 画面から自動起動を無効化できます。

入力がおかしいと感じた場合は、VR Stick Scope を終了し、SteamVR と VRChat を起動し直してください。詳しくは公開 ZIP 内の `docs\ROLLBACK_JA.txt` を参照してください。

## 安全とプライバシー

- VR Stick Scope は VRChat、SteamVR、Meta、Valve の公式ツールではありません。
- 診断と補正処理はローカルで行われます。
- コントローラー入力データを外部サーバーへアップロードしません。
- 補正は物理故障を修理するものではありません。
- プロファイルとローカル設定は、ユーザーのローカルアプリケーションデータフォルダ内の `VRStickScope` に保存されます。

## ライセンス

VR Stick Scope は MIT License で公開しています。詳細は `LICENSE` を参照してください。
