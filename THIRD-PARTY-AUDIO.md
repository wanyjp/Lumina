> **第三方音频清单**
> 白噪音资源包共 62 个文件、1162.8 MB，**不纳入 Git 仓库**，随官方安装包或 GitHub Release 资源包分发。
> 本文件是公开仓库中的元数据与授权记录；CC BY 素材要求保留作者、来源与许可信息。
> 运行时由 `NoiseLibrary.Locate()` 按 `LUMINA_AUDIO_ROOT` → `%APPDATA%\Lumina\audio` → 应用目录 `Audio` 依次查找；均缺失时降级为空分类并提示资源包缺失。
> 官方安装包内的音频不适用 MIT 许可证，按下列原始许可使用；`.gitignore` 已对 `*.wav` / `*.ogg` / `*.mp3` 与 `app_audio/` 做兜底拦截。

---

# 音源清单

**核对时间：** 2026-11-06  
**核对范围：** 资源包内 8 个分类、62 个文件（逐一核对实际文件，不沿用旧清单数字）  
**统计口径：** 只计真实存在且可解码的音频；时长按实际解码计算，大小为磁盘字节；跨批次同一录音（SHA-256 相同）只计一次。

## 一、总量概览

| 项目 | 数值 |
|---|---|
| 总条数 | **62 条**（唯一录音 62 条，`app_audio` 内无字节重复） |
| 总时长 | **1:48:50**（6530 秒） |
| 总大小 | **1162.8 MB**（1,219,300,491 字节） |
| 批次分布 | `app_audio` 62 条；`audio/` 0 条、`quietfield/` 0 条（两目录已不存在） |
| 父类别 | 8 个 |

**各类别分布（仅 `app_audio`）：**

| 父类别 | 条数 | 总时长 | 总大小 |
|---|---:|---|---:|
| 雨 | 10 | 0:14:20 | 126.8 MB |
| 水流 | 15 | 0:21:17 | 199.6 MB |
| 风 | 6 | 0:14:08 | 171.7 MB |
| 火焰 | 7 | 0:09:13 | 94.4 MB |
| 森林与夜 | 9 | 0:25:28 | 323.9 MB |
| 动物 | 6 | 0:10:59 | 68.0 MB |
| 城市与交通 | 7 | 0:11:50 | 168.9 MB |
| 户外脚步 | 2 | 0:01:34 | 9.5 MB |
| **合计** | **62** | **1:48:50** | **1162.8 MB** |

## 三、按父类别清单

### 雨（10 条，126.8 MB）

| 文件名 | 时长 | 格式 / 规格 | 大小 | 所属批次 | 许可 | 原始出处 | 原包内相对路径 |
|---|---|---|---:|---|---|---|---|
| 伞下听雨.wav | 0:31 | WAV · 24-bit · 48.0 kHz / 2ch | 8.58 MB | app_audio | CC0 1.0 | [BigSoundBank · Pierre SIBANARCO](https://bigsoundbank.com/rain-under-an-umbrella-s2679.html) | `resources/app/dist/sounds/bigsoundbank/2679.wav` |
| 小雨.ogg | 2:05 | OGG Vorbis · 44.1 kHz / 2ch | 2.99 MB | app_audio | CC BY 4.0 | [Freesound · alex36917](https://freesound.org/people/alex36917/sounds/524605/) | `resources/app/dist/sounds/blanket/rain.ogg` |
| 屋内听雨.wav | 0:19 | WAV · 16-bit · 48.0 kHz / 2ch | 3.56 MB | app_audio | CC BY 3.0 | [SoundBible · Mark DiAngelo](https://soundbible.com/2065-Rain-Inside-House.html) | `resources/app/dist/sounds/wav/wav-rain-house.wav` |
| 帐篷雷雨.wav | 3:29 | WAV · 16-bit · 48.0 kHz / 2ch | 38.36 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/rain-and-storm-in-a-tent-s0820.html) | `resources/app/dist/sounds/bigsoundbank/0820.wav` |
| 绵绵雨声.wav | 0:33 | WAV · 24-bit · 44.1 kHz / 2ch | 8.40 MB | app_audio | CC BY 3.0 | [SoundBible · Mike Koenig](https://soundbible.com/2011-Rain-Background.html) | `resources/app/dist/sounds/wav/wav-steady-rain.wav` |
| 车内听雨.wav | 1:49 | WAV · 24-bit · 48.0 kHz / 1ch | 14.98 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/rain-on-car-roof-s1293.html) | `resources/app/dist/sounds/bigsoundbank/1293.wav` |
| 远处雷雨.ogg | 0:26 | OGG Vorbis · 44.1 kHz / 2ch | 0.46 MB | app_audio | CC BY 4.0 | [Freesound · digifishmusic](https://freesound.org/people/digifishmusic/sounds/41739/) | `resources/app/dist/sounds/blanket/storm.ogg` |
| 雨敲车窗.wav | 1:13 | WAV · 24-bit · 48.0 kHz / 1ch | 10.03 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/rain-on-car-windshield-s1295.html) | `resources/app/dist/sounds/bigsoundbank/1295.wav` |
| 雨落水洼.wav | 1:18 | WAV · 24-bit · 48.0 kHz / 1ch | 10.71 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/rain-on-puddle-s1290.html) | `resources/app/dist/sounds/bigsoundbank/1290.wav` |
| 露台夏雨.wav | 2:37 | WAV · 16-bit · 48.0 kHz / 2ch | 28.70 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/summer-rain-on-terrace-s1019.html) | `resources/app/dist/sounds/bigsoundbank/1019.wav` |

### 水流（15 条，199.6 MB）

| 文件名 | 时长 | 格式 / 规格 | 大小 | 所属批次 | 许可 | 原始出处 | 原包内相对路径 |
|---|---|---|---:|---|---|---|---|
| 小瀑布.wav | 0:28 | WAV · 16-bit · 48.0 kHz / 1ch | 2.61 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/small-cascade-s0507.html) | `resources/app/dist/sounds/bigsoundbank/0507.wav` |
| 山泉轻流.wav | 1:45 | WAV · 24-bit · 48.0 kHz / 2ch | 28.80 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/small-stream-4-s1354.html) | `resources/app/dist/sounds/bigsoundbank/1354.wav` |
| 山涧急流.wav | 2:28 | WAV · 24-bit · 48.0 kHz / 2ch | 40.57 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN & Axeline T.](https://bigsoundbank.com/mountain-stream-7-s3222.html) | `resources/app/dist/sounds/bigsoundbank/3222.wav` |
| 山间溪流.ogg | 2:26 | OGG Vorbis · 44.1 kHz / 2ch | 4.79 MB | app_audio | CC0 1.0 | [Freesound · gluckose](https://freesound.org/people/gluckose/sounds/333987/) | `resources/app/dist/sounds/blanket/stream.ogg` |
| 庭院水泉.wav | 1:02 | WAV · 16-bit · 48.0 kHz / 2ch | 11.29 MB | app_audio | CC0 1.0 | [BigSoundBank · Olivier du Japon](https://bigsoundbank.com/fountain-of-a-japanese-temple-s0913.html) | `resources/app/dist/sounds/bigsoundbank/0913.wav` |
| 林溪近听.wav | 1:28 | WAV · 24-bit · 48.0 kHz / 1ch | 12.11 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/watercourse-1-s3132.html) | `resources/app/dist/sounds/bigsoundbank/3132.wav` |
| 水滴轻落.wav | 0:22 | WAV · 24-bit · 48.0 kHz / 1ch | 3.01 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/drops-of-water-3-s1386.html) | `resources/app/dist/sounds/bigsoundbank/1386.wav` |
| 沙滩细浪.wav | 1:38 | WAV · 24-bit · 48.0 kHz / 2ch | 26.81 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/small-waves-froth-and-beach-s1447.html) | `resources/app/dist/sounds/bigsoundbank/1447.wav` |
| 河流.mp3 | 1:53 | MP3 · 274 kbps · 44.1 kHz / 2ch | 3.71 MB | app_audio | 未标注（需核实） | —（文件无标签，来源未知） | — |
| 洞穴滴水.wav | 1:11 | WAV · 24-bit · 48.0 kHz / 2ch | 19.59 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/cave-2-s2136.html) | `resources/app/dist/sounds/bigsoundbank/2136.wav` |
| 浅滩水流.wav | 1:00 | WAV · 24-bit · 48.0 kHz / 1ch | 8.25 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/watercourse-5-2-s3137.html) | `resources/app/dist/sounds/bigsoundbank/3137.wav` |
| 海浪.ogg | 1:58 | OGG Vorbis · 44.1 kHz / 2ch | 2.64 MB | app_audio | CC BY 4.0 | [Freesound · Luftrum](https://freesound.org/people/Luftrum/sounds/48412/) | `resources/app/dist/sounds/blanket/waves.ogg` |
| 海浪与燕鸥.wav | 0:58 | WAV · 16-bit · 44.1 kHz / 2ch | 9.68 MB | app_audio | CC0 1.0 | [BigSoundBank · DenisChardonnet](https://bigsoundbank.com/sea-waves-and-seagulls-s0267.html) | `resources/app/dist/sounds/bigsoundbank/0267.wav` |
| 瀑布.mp3 | 0:23 | MP3 · 187 kbps · 48.0 kHz / 2ch | 0.53 MB | app_audio | 未标注（需核实） | —（文件无标签，来源未知） | — |
| 面海轻浪.wav | 2:18 | WAV · 16-bit · 48.0 kHz / 2ch | 25.19 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/small-waves-facing-the-ocean-s1046.html) | `resources/app/dist/sounds/bigsoundbank/1046.wav` |

### 风（6 条，171.7 MB）

| 文件名 | 时长 | 格式 / 规格 | 大小 | 所属批次 | 许可 | 原始出处 | 原包内相对路径 |
|---|---|---|---:|---|---|---|---|
| 原野风声.ogg | 0:15 | OGG Vorbis · 44.1 kHz / 2ch | 0.28 MB | app_audio | CC0 1.0 | [Freesound · felix.blume](https://freesound.org/people/felix.blume/sounds/217506/) | `resources/app/dist/sounds/blanket/wind.ogg` |
| 旷野长风.wav | 2:03 | WAV · 16-bit · 44.1 kHz / 2ch | 20.69 MB | app_audio | CC BY 3.0 | [SoundBible · Mark DiAngelo](https://soundbible.com/1810-Wind.html) | `resources/app/dist/sounds/wav/wav-wind.wav` |
| 草间风声.wav | 1:25 | WAV · 16-bit · 48.0 kHz / 2ch | 15.58 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/wind-in-tall-grass-s0908.html) | `resources/app/dist/sounds/bigsoundbank/0908.wav` |
| 风吹树梢.wav | 3:38 | WAV · 16-bit · 48.0 kHz / 2ch | 40.00 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/forest-wind-in-the-trees-s0904.html) | `resources/app/dist/sounds/bigsoundbank/0904.wav` |
| 风过灌木.wav | 3:00 | WAV · 16-bit · 48.0 kHz / 2ch | 32.98 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/wind-in-shrub-s0907.html) | `resources/app/dist/sounds/bigsoundbank/0907.wav` |
| 风铃轻奏.wav | 3:46 | WAV · 24-bit · 48.0 kHz / 2ch | 62.18 MB | app_audio | CC0 1.0 | [BigSoundBank · Pierre SIBANARCO](https://bigsoundbank.com/cluster-chimes-1-s2687.html) | `resources/app/dist/sounds/bigsoundbank/2687.wav` |

### 火焰（7 条，94.4 MB）

| 文件名 | 时长 | 格式 / 规格 | 大小 | 所属批次 | 许可 | 原始出处 | 原包内相对路径 |
|---|---|---|---:|---|---|---|---|
| 壁炉原录音.wav | 0:25 | WAV · 16-bit · 44.1 kHz / 1ch | 2.14 MB | app_audio | Public Domain | [SoundBible · ezwa](https://soundbible.com/1543-Fireplace.html) | `resources/app/dist/sounds/wav/wav-fireplace.wav` |
| 夜营篝火.mp3 | 1:41 | MP3 · 229 kbps · 44.1 kHz / 2ch | 2.78 MB | audio → app_audio | CC0 1.0 | [Freesound · 187079](https://freesound.org/s/187079/) | — |
| 木柴暖炉.wav | 1:19 | WAV · 24-bit · 48.0 kHz / 2ch | 21.74 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/fireplace-4-s2856.html) | `resources/app/dist/sounds/bigsoundbank/2856.wav` |
| 柴火壁炉.wav | 0:45 | WAV · 24-bit · 48.0 kHz / 2ch | 12.44 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/fireplace-5-s2857.html) | `resources/app/dist/sounds/bigsoundbank/2857.wav` |
| 炉火慢燃.wav | 3:02 | WAV · 24-bit · 48.0 kHz / 2ch | 49.89 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/fireplace-3-s2855.html) | `resources/app/dist/sounds/bigsoundbank/2855.wav` |
| 秋夜篝火.mp3 | 1:22 | MP3 · 190 kbps · 44.1 kHz / 2ch | 1.86 MB | audio → app_audio | CC0 1.0 | [Freesound · 62487](https://freesound.org/s/62487/) | — |
| 篝火燃烧.wav | 0:38 | WAV · 16-bit · 48.0 kHz / 1ch | 3.52 MB | app_audio | CC0 1.0 | [BigSoundBank · JosephSardin](https://bigsoundbank.com/big-branching-fire-1-s0987.html) | `resources/app/dist/sounds/bigsoundbank/0987.wav` |

### 森林与夜（9 条，323.9 MB）

| 文件名 | 时长 | 格式 / 规格 | 大小 | 所属批次 | 许可 | 原始出处 | 原包内相对路径 |
|---|---|---|---:|---|---|---|---|
| 丛林.mp3 | 4:27 | MP3 · 122 kbps · 24.0 kHz / 2ch | 3.89 MB | app_audio | 未标注（需核实） | —（文件无标签，来源未知） | — |
| 夏夜虫鸣.wav | 0:49 | WAV · 16-bit · 44.1 kHz / 2ch | 8.29 MB | app_audio | Public Domain | [SoundBible · Lisa Redfern](https://soundbible.com/2083-Crickets-Chirping-At-Night.html) | `resources/app/dist/sounds/wav/wav-crickets.wav` |
| 林中鸟语.wav | 2:41 | WAV · 24-bit · 48.0 kHz / 2ch | 44.11 MB | app_audio | CC0 1.0 | [BigSoundBank · Pierre SIBANARCO](https://bigsoundbank.com/forest-3-s2715.html) | `resources/app/dist/sounds/bigsoundbank/2715.wav` |
| 林间环境.wav | 0:36 | WAV · 16-bit · 44.1 kHz / 2ch | 6.07 MB | app_audio | Public Domain | [SoundBible · nille](https://soundbible.com/1263-Nature-Ambiance.html) | `resources/app/dist/sounds/wav/wav-nature.wav` |
| 林间风与鸟.wav | 3:46 | WAV · 24-bit · 48.0 kHz / 2ch | 62.04 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/forest-4-s2749.html) | `resources/app/dist/sounds/bigsoundbank/2749.wav` |
| 溪畔森林.wav | 5:00 | WAV · 24-bit · 48.0 kHz / 2ch | 82.40 MB | app_audio | CC0 1.0 | [BigSoundBank · Pierre SIBANARCO](https://bigsoundbank.com/forest-and-stream-1-s2713.html) | `resources/app/dist/sounds/bigsoundbank/2713.wav` |
| 田野夜虫.wav | 4:59 | WAV · 24-bit · 48.0 kHz / 2ch | 82.15 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/campaign-at-night-4-s1880.html) | `resources/app/dist/sounds/bigsoundbank/1880.wav` |
| 田野池塘.wav | 2:42 | WAV · 16-bit · 48.0 kHz / 2ch | 29.57 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/pond-in-a-field-s0691.html) | `resources/app/dist/sounds/bigsoundbank/0691.wav` |
| 雨后夜林.wav | 0:29 | WAV · 16-bit · 48.0 kHz / 2ch | 5.37 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/forest-at-night-after-rain-s0555.html) | `resources/app/dist/sounds/bigsoundbank/0555.wav` |

### 动物（6 条，68.0 MB）

| 文件名 | 时长 | 格式 / 规格 | 大小 | 所属批次 | 许可 | 原始出处 | 原包内相对路径 |
|---|---|---|---:|---|---|---|---|
| 午夜虫声.mp3 | 1:00 | MP3 · 181 kbps · 48.0 kHz / 2ch | 1.31 MB | quietfield → app_audio | CC0 1.0 | [Freesound · sengjinn](https://freesound.org/people/sengjinn/sounds/175020/) | `resources/app/dist/sounds/review/175020.mp3` |
| 晨间鸟鸣.ogg | 2:10 | OGG Vorbis · 44.1 kHz / 2ch | 2.92 MB | app_audio | CC0 1.0 | [Freesound · kvgarlic](https://freesound.org/people/kvgarlic/sounds/156826/) | `resources/app/dist/sounds/blanket/birds.ogg` |
| 林间猫头鹰.wav | 1:16 | WAV · 16-bit · 48.0 kHz / 1ch | 6.95 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/long-eared-owl-2-s0937.html) | `resources/app/dist/sounds/bigsoundbank/0937.wav` |
| 池塘蛙鸣.wav | 2:29 | WAV · 16-bit · 48.0 kHz / 1ch | 13.68 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/frogs-2-s0998.html) | `resources/app/dist/sounds/bigsoundbank/0998.wav` |
| 港湾海鸥.wav | 1:54 | WAV · 24-bit · 48.0 kHz / 2ch | 31.33 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/gulls-on-the-harbor-s2573.html) | `resources/app/dist/sounds/bigsoundbank/2573.wav` |
| 猫咪呼噜.wav | 2:09 | WAV · 16-bit · 48.0 kHz / 1ch | 11.81 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/cat-purring-2-s0981.html) | `resources/app/dist/sounds/bigsoundbank/0981.wav` |

### 城市与交通（7 条，168.9 MB）

| 文件名 | 时长 | 格式 / 规格 | 大小 | 所属批次 | 许可 | 原始出处 | 原包内相对路径 |
|---|---|---|---:|---|---|---|---|
| 列车车厢.wav | 4:10 | WAV · 24-bit · 48.0 kHz / 2ch | 68.80 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/train-car-s2727.html) | `resources/app/dist/sounds/bigsoundbank/2727.wav` |
| 城市街道.ogg | 0:25 | OGG Vorbis · 44.1 kHz / 2ch | 0.34 MB | app_audio | CC BY 3.0 | [Freesound · gezortenplotz](https://freesound.org/people/gezortenplotz/sounds/44796/) | `resources/app/dist/sounds/blanket/city.ogg` |
| 宁静渔港.wav | 2:27 | WAV · 24-bit · 48.0 kHz / 2ch | 40.26 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/small-fishing-port-1-s2571.html) | `resources/app/dist/sounds/bigsoundbank/2571.wav` |
| 湖上行舟.ogg | 0:42 | OGG Vorbis · 44.1 kHz / 2ch | 0.61 MB | app_audio | CC0 1.0 | [Freesound · Falcet](https://freesound.org/people/Falcet/sounds/439365/) | `resources/app/dist/sounds/blanket/boat.ogg` |
| 电车行进.ogg | 0:07 | OGG Vorbis · 24.0 kHz / 2ch | 0.12 MB | app_audio | CC BY 3.0 | [Freesound · SDLx](https://freesound.org/people/SDLx/sounds/259988/) | `resources/app/dist/sounds/blanket/train.ogg` |
| 老地铁行进.wav | 2:43 | WAV · 24-bit · 48.0 kHz / 2ch | 44.82 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/old-metro-interior-s3040.html) | `resources/app/dist/sounds/bigsoundbank/3040.wav` |
| 高速列车.wav | 1:16 | WAV · 16-bit · 48.0 kHz / 2ch | 14.01 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/eurostar-car-s0635.html) | `resources/app/dist/sounds/bigsoundbank/0635.wav` |

### 户外脚步（2 条，9.5 MB）

| 文件名 | 时长 | 格式 / 规格 | 大小 | 所属批次 | 许可 | 原始出处 | 原包内相对路径 |
|---|---|---|---:|---|---|---|---|
| 踩过落叶.wav | 1:04 | WAV · 16-bit · 44.1 kHz / 1ch | 5.35 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/feet-in-leaves-s0137.html) | `resources/app/dist/sounds/bigsoundbank/0137.wav` |
| 雪地脚步.wav | 0:30 | WAV · 24-bit · 48.0 kHz / 1ch | 4.18 MB | app_audio | CC0 1.0 | [BigSoundBank · Joseph SARDIN](https://bigsoundbank.com/steps-in-the-snow-3-s3363.html) | `resources/app/dist/sounds/bigsoundbank/3363.wav` |

## 四、瑕疵与使用注意

**1. 来源/许可未知（3 条）**

- `森林与夜/丛林.mp3`：MP3 · 122 kbps · 24.0 kHz / 2ch，4:27，无标签、无出处记录，需自行确认授权。
- `水流/河流.mp3`：MP3 · 274 kbps · 44.1 kHz / 2ch，1:53，无标签、无出处记录，需自行确认授权。
- `水流/瀑布.mp3`：MP3 · 187 kbps · 48.0 kHz / 2ch，0:23，无标签、无出处记录，需自行确认授权。

**2. 电平偏低（峰值 < -20 dBFS）**

- `动物/午夜虫声.mp3`：峰值 **-29.4 dBFS**，1:00，直接播放偏小；建议播放器增益或后续统一归一化。
- `雨/屋内听雨.wav`：峰值 **-21.0 dBFS**，0:19，直接播放偏小；建议播放器增益或后续统一归一化。

**3. 短片段（不足 30 秒）**

- `城市与交通/电车行进.ogg`：0:07，OGG Vorbis · 24.0 kHz / 2ch。
- `风/原野风声.ogg`：0:15，OGG Vorbis · 44.1 kHz / 2ch。
- `雨/屋内听雨.wav`：0:19，WAV · 16-bit · 48.0 kHz / 2ch。
- `水流/水滴轻落.wav`：0:22，WAV · 24-bit · 48.0 kHz / 1ch。
- `水流/瀑布.mp3`：0:23，MP3 · 187 kbps · 48.0 kHz / 2ch。
- `城市与交通/城市街道.ogg`：0:25，OGG Vorbis · 44.1 kHz / 2ch。
- `火焰/壁炉原录音.wav`：0:25，WAV · 16-bit · 44.1 kHz / 1ch。
- `雨/远处雷雨.ogg`：0:26，OGG Vorbis · 44.1 kHz / 2ch。
- `水流/小瀑布.wav`：0:28，WAV · 16-bit · 48.0 kHz / 1ch。
- `森林与夜/雨后夜林.wav`：0:29，WAV · 16-bit · 48.0 kHz / 2ch。

**4. 有损/处理版本**

- `午夜虫声.mp3`、`秋夜篝火.mp3`、`夜营篝火.mp3` 为 MP3 有损副本，不是无损母带。
- 附录中的 `深林细雨`、`海边放空` 是 20 秒网站试听混音，并非完整长度录音，未计入本清单总量。

## 五、需要署名的条目（CC BY）

以下条目为 **CC BY 3.0 / 4.0**，可商用但再分发必须署名。`app_audio` 内共 8 条：

| 文件名 | 作者 | 许可 | 出处 | 建议署名 |
|---|---|---|---|---|
| `雨/小雨.ogg` | alex36917 | CC BY 4.0 | [链接](https://freesound.org/people/alex36917/sounds/524605/) | `alex36917（原录音），Freesound，CC BY 4.0` |
| `雨/远处雷雨.ogg` | digifishmusic | CC BY 4.0 | [链接](https://freesound.org/people/digifishmusic/sounds/41739/) | `digifishmusic（原录音），Freesound，CC BY 4.0` |
| `水流/海浪.ogg` | Luftrum | CC BY 4.0 | [链接](https://freesound.org/people/Luftrum/sounds/48412/) | `Luftrum（原录音），Freesound，CC BY 4.0` |
| `雨/屋内听雨.wav` | Mark DiAngelo | CC BY 3.0 | [链接](https://soundbible.com/2065-Rain-Inside-House.html) | `Mark DiAngelo（原录音），SoundBible，CC BY 3.0` |
| `雨/绵绵雨声.wav` | Mike Koenig | CC BY 3.0 | [链接](https://soundbible.com/2011-Rain-Background.html) | `Mike Koenig（原录音），SoundBible，CC BY 3.0` |
| `风/旷野长风.wav` | Mark DiAngelo | CC BY 3.0 | [链接](https://soundbible.com/1810-Wind.html) | `Mark DiAngelo（原录音），SoundBible，CC BY 3.0` |
| `城市与交通/城市街道.ogg` | gezortenplotz | CC BY 3.0 | [链接](https://freesound.org/people/gezortenplotz/sounds/44796/) | `gezortenplotz（原录音），Freesound，CC BY 3.0` |
| `城市与交通/电车行进.ogg` | SDLx | CC BY 3.0 | [链接](https://freesound.org/people/SDLx/sounds/259988/) | `SDLx（原录音），Freesound，CC BY 3.0` |

其余 51 条为 CC0 1.0（48 条）或 Public Domain（3 条），无需署名；另有 3 条来源未知，见第四节。