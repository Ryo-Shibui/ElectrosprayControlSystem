# Electrospray Control System

NI USB-6001、Dino-Liteカメラ、NE-1000シリンジポンプを使って、エレクトロスプレー実験を半自動化するWindowsデスクトップソフトです。

PCを買ったばかりでVisual Studioも入っていない状態から使えるように、必要なソフト、装置、配線、ビルド、操作手順をこのREADMEにまとめています。

## 何ができるか

- NI USB-6001のアナログ出力で高電圧モジュールの制御電圧を出す
- VmoniとImoniをアナログ入力で常時監視する
- Dino-Liteのライブプレビューを表示する
- 指定した時間だけ測定し、CSVとメタデータを保存する
- 測定時間の中央でDino-Lite画像を自動保存する
- NE-1000シリンジポンプをRS-232で操作する
- DAQ、カメラ、ポンプをできるだけ独立に復旧できる

## 専用の装置か

このソフトは特定の実験構成を前提にしています。別のDAQ、別のカメラ、別のポンプでもC#コードを修正すれば流用できますが、そのまま使う前提の装置は次です。

準備するもの:

- NI USB-6001
- NI-DAQmx
- 高電圧モジュール
- 高電圧モジュールの制御入力、Vmoni出力、Imoni出力をUSB-6001へつなぐ配線
- Dino-Lite Premier系カメラ
- Dino-Lite SDKランタイムファイル
- NE-1000シリンジポンプ
- USB-RS232変換器またはRS-232ポート
- Windows 10またはWindows 11の64 bit PC
- 測定データ保存先フォルダ

公開リポジトリには、NIやDino-LiteのメーカーDLLは含めていません。各メーカーのインストーラまたはSDKから、自分のPCに正規に入手したファイルを配置してください。

## 新品PCで最初に入れるもの

1. Windows Updateを実行する。
2. Visual Studio 2022をインストールする。
3. Visual Studio Installerで `.NET desktop development` ワークロードを選ぶ。
4. .NET Framework 4.8.1 Developer Packをインストールする。Visual Studioで `net481` が選べれば追加作業は不要です。
5. NI-DAQmxをインストールする。インストール時に.NET/APIサポートを有効にする。
6. NI MAXを起動し、USB-6001が認識されていることを確認する。
7. Dino-Lite用ソフトウェア、ドライバ、SDKをインストールする。
8. NE-1000ポンプ用のUSB-RS232変換器を接続し、WindowsでCOMポートが見えることを確認する。

## メーカーDLLの配置

NI-DAQmxの.NET DLLが通常の場所から読めないPCでは、次のフォルダへ自分のPCにインストール済みのDLLをコピーしてください。

```text
ElectrosprayControlSystem\ThirdParty\NI\
```

配置するファイル:

```text
NationalInstruments.DAQmx.dll
NationalInstruments.Common.dll
```

Dino-Lite SDKを使う場合は、次のフォルダへSDKランタイムをコピーしてください。

```text
ElectrosprayControlSystem\ThirdParty\DinoLite\
```

配置するファイル:

```text
DNX64.dll
libusbK.dll
```

DLLがない場合でも、Dino-LiteがWindowsのカメラとして見えていればDirectShow経由のプレビューへフォールバックします。ただし、Dino-Lite SDK固有機能を使うにはSDK DLLが必要です。

## ダウンロードして開く

1. GitHubページの `Code` からZIPをダウンロードする、またはGitでcloneする。
2. ZIPの場合は任意の作業フォルダへ展開する。
3. 必要に応じてメーカーDLLを `ThirdParty` フォルダへ配置する。
4. `ElectrosprayControlSystem.sln` をVisual Studio 2022で開く。
5. NuGet restoreを許可する。
6. 画面上部の構成を `Release`、プラットフォームを `x64` にする。
7. `Build` -> `Build Solution` を実行する。
8. `Start` またはF5で起動する。

PowerShellでビルドする場合:

```powershell
.\build-release.ps1
```

ビルド後の実行ファイルは通常、次の場所に作成されます。

```text
ElectrosprayControlSystem\bin\x64\Release\net481\ElectrosprayControlSystem.exe
```

## USB-6001の初期設定

初期値は次の通りです。自分のPCではNI MAXでデバイス名を確認し、必要ならGUIで変更してください。

- DAQ device name: `Dev3`
- Analog output channel: `ao0`
- Vmoni input channel: `ai3`
- Imoni differential pair: `ai0` / `ai4`
- HV minimum: 0 kV
- HV maximum: 6 kV
- Control signal maximum: 5 V
- Vmoni scale: 1200
- Imoni scale: 100

Imoniの差動入力はUSB-6001の仕様に合わせ、次の組み合わせから選びます。

- `ai0` / `ai4`
- `ai1` / `ai5`
- `ai2` / `ai6`
- `ai3` / `ai7`

VmoniはRSE入力、Imoniは差動入力として作成されます。

## 基本操作

1. USB-6001、Dino-Lite、NE-1000ポンプをPCへ接続する。
2. NI MAXでUSB-6001のデバイス名を確認する。
3. ソフトを起動する。
4. `USB-6001 Channels` でデバイス名とチャンネルを確認する。
5. HV範囲、制御電圧上限、Vmoni/Imoni換算係数を確認する。
6. `Refresh Hardware` でDAQとカメラを再検出する。
7. `Applied voltage [kV]` に目標電圧を入れる。
8. `Apply` を押すとUSB-6001のAOから制御電圧が出る。
9. `Stop Apply` を押すとAOが0 Vに戻る。
10. 測定時間、サンプル間隔、保存先を設定する。
11. `Measure` を押すとCSV、メタデータ、中央時刻のDino-Lite画像が保存される。
12. 途中終了したい場合は `Stop Measure` を押す。

測定ファイルは保存先の中にタイムスタンプ付きフォルダとして作成されます。

## NE-1000シリンジポンプ

`Liquid Supply` パネルでNE-1000を操作します。

1. USB-RS232変換器を接続する。
2. `Pump Settings` を開く。
3. `Refresh` でCOMポートを更新する。
4. ポンプのCOMポートを選ぶ。
5. 通信条件を確認する。初期値は19200 baud、8 data bits、no parity、1 stop bit、no flow controlです。
6. `Connect` を押す。
7. シリンジ径、流量、単位、Infuse/Withdraw、体積またはContinuousを設定する。
8. `Start` で送液開始、`Stop` で停止する。
9. `Purge` は安全確認後に使ってください。

ポンプ、DAQ、カメラは独立に扱われます。1つが未接続でも、他の装置が使える場合は起動を継続します。

## 実験前の確認

- USB-6001がNI MAXで見えている
- GUIの `DAQ device name` がNI MAXの名前と一致している
- AO出力が高電圧モジュールの制御入力へ正しく入っている
- `Apply` 前に高電圧側の安全確認が済んでいる
- VmoniとImoniの配線と換算係数が正しい
- Dino-Liteプレビューが表示される
- 測定中央で画像が保存される
- NE-1000が選択COMポートで応答する
- 保存先フォルダにCSV、メタデータ、画像が作成される

## トラブルシュート

- DAQが見つからない場合: NI-DAQmx、NI MAX、USB接続、デバイス名を確認してください。
- `NationalInstruments.DAQmx.dll` が見つからない場合: NI-DAQmxの.NETサポートを入れるか、`ThirdParty\NI` へDLLを配置してください。
- Dino-Lite SDKが見つからない場合: `ThirdParty\DinoLite` に `DNX64.dll` と `libusbK.dll` があるか確認してください。
- カメラが表示されない場合: WindowsのカメラデバイスとしてDino-Liteが認識されているか確認してください。
- ポンプが応答しない場合: COMポート、ボーレート、ポンプアドレス、RS-232ケーブルを確認してください。
- 測定ファイルが作られない場合: 保存先フォルダの書き込み権限を確認してください。
- 高電圧が意図と違う場合: `HV minimum`、`HV maximum`、`Control signal maximum` と実機の制御入力仕様を確認してください。

## 補足

詳細なセットアップメモは `SETUP_GUIDE.md` にもあります。このREADMEを優先し、不足する細部を確認したい場合に参照してください。
