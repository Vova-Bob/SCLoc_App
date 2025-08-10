# SCLocalizationUA
[![SCLocalizationUA](https://img.shields.io/github/release/Vova-Bob/SCLoc_App?include_prereleases&sort=date&label=SCLocalizationUA)](https://github.com/Vova-Bob/SCLoc_App/releases/latest)
[![Crowdin](https://badges.crowdin.net/star-citizen-localization-ua/localized.svg)](https://shorturl.at/dopMW)
[![Visitors](https://api.visitorbadge.io/api/visitors?path=Vova-Bob%2FSCLoc_App&labelColor=%2337d67a&countColor=%23263759&style=plastic&labelStyle=lower)](https://visitorbadge.io/status?path=Vova-Bob%2FSCLoc_App)
[![Website](https://img.shields.io/website?url=https%3A%2F%2Fusf.42web.io%2F&down_message=SITE&style=flat&label=USF&labelColor=blue&color=yellow)](https://usf.42web.io/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/Vova-Bob/SCLoc_App/blob/main/LICENSE)
[![Virustotal](https://img.shields.io/static/v1?label=Virustotal&message=Scan&color=blue)](https://www.virustotal.com/gui/file/709552463786858d54e9285df1e69d8f4acd9130a614b42f4490344b8f7fb70a?nocache=1)
![SClocApp](https://github.com/Vova-Bob/SCLoc_App/blob/main/img/sclocua.jpg)

Додаток для локалізації гри Star Citizen Українською

Підтримка Української локалізації гри зроблена учасниками [UKRAINIAN SPACE FLEET](https://robertsspaceindustries.com/orgs/UKR)

[![Website](https://img.shields.io/website?url=https%3A%2F%2Fsend.monobank.ua%2Fjar%2F44HXkQkorg&up_message=%D0%9F%D1%80%D0%BE%D0%B5%D0%BA%D1%82&style=for-the-badge&label=%D0%9F%D1%96%D0%B4%D1%82%D1%80%D0%B8%D0%BC%D0%B0%D1%82%D0%B8)
](https://send.monobank.ua/jar/44HXkQkorg)

[Посилання на переклад](https://github.com/Vova-Bob/SC_localization_UA)

Повідомити про помилку в перекладі або приєднатись до проєкту можна через наш [Discord](https://discord.gg/TkaN6Yv4VT)

### Важливо:
Ця програма не втручається жодним чином в файли або пам'ять гри. Ми дотримуємося угоди про використання гри і не намагаємося отримати незаслужені переваги чи порушити її баланс.

Основна функціональність:
Ми пропонуємо можливість додати локалізацію до гри Star Citizen, включаючи переклад вмісту гри. Ці зміни не впливають на геймплей чи ігровий баланс, а лише допомагають гравцям насолоджуватися грою на своїй рідній мові.

## Інструкція

1. Завантажте останню версію з [релізів](https://github.com/Vova-Bob/SCLoc_App/releases/latest)
2. Запустіть інсталятор та встановіть додаток у зручну для вас папку.
3. Запустіть додаток через ярлик на робочому столі або з папки встановлення та виберіть папку з грою `[StarCitizen\LIVE]`
   (при наступному запуску додатка шлях буде вибраний автоматично)
4. Включіть прапорець `user.cfg` якщо у вас не має цього файлу (конфігурації користувача) або створіть його власноруч з [інструкції](https://github.com/Vova-Bob/SC_localization_UA)
5. Натисніть кнопку "встановити локалізацію"
   
![Опис зображення](https://github.com/Vova-Bob/SCLoc_App/blob/main/img/8.png)

---

## Executive Hangar Timer Overlay

**Призначення:**  
Відображає статус ангару та час до його відкриття/закриття з можливістю ручної або віддаленої синхронізації.

### Глобальні гарячі клавіші:
- `F6` — показати/приховати оверлей  
- `F8` — увімк./вимк. режим кліків крізь вікно  
- `Ctrl+F8` — тимчасово вимкнути кліки крізь для перетягування  
- `F7` — встановити старт = зараз  
- `Shift+F7` — ввести час у форматі `HH:mm[:ss]` або Unix мс  
- `F9` — синхронізувати з віддаленим часом  
- `Shift+F9` — очистити локальний час та синхронізувати

### Масштабування:
- `Ctrl -` / `Ctrl =` / `Ctrl 0`

### Прозорість:
- `Ctrl+Alt -` / `Ctrl+Alt =` / `Ctrl+Alt 0`

### Фази циклу:
- **Червона (RED)** — 2 години (зачинено)  
- **Зелена (GREEN)** — 1 година (відкрито)  
- **Чорна (BLACK)** — 5 хвилин (перезапуск)

### Синхронізація часу:
Час береться з вказаного URL (`START_TIME_URL`) у форматі JSON:
```json
{"cycleStartMs": 1722880000000}
