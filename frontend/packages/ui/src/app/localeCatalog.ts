// Source phrases are stable migration keys; unknown phrases stay visible until translated.
export const catalog:Record<string,readonly [string,string,string,string?]>={
  "Мой QR друга": ["My friend QR","我的好友二维码","내 친구 QR"],
  "Добавление друга по QR": ["Add a friend by QR","扫码添加好友","QR로 친구 추가"],
  "QR содержит ссылку на публичный ID. Друг должен открыть профиль и отправить запрос; контакт появится после принятия.": ["The QR links to a public ID. Your friend must send a request, and the contact appears after acceptance.","二维码指向公开 ID。好友须发送请求，接受后建立联系。","QR에는 공개 ID 링크가 포함됩니다. 친구가 요청을 보내고 수락하면 연락처가 추가됩니다."],
  "Мобильное приложение: технический профиль устройства, сведения о сессии и отдельный выбор разрешений. Камера используется только для QR; отказ от необязательных прав не ограничивает обычный вход.": ["Mobile app: device technical profile, session details and separate permission choices. Camera is for QR only; refusing optional permissions does not block regular sign-in.","移动应用：设备技术信息、会话信息及独立权限选择。摄像头仅用于二维码；拒绝可选权限不影响正常登录。","모바일 앱: 기기 기술 정보, 세션 정보 및 별도 권한 선택. 카메라는 QR 전용이며 선택 권한을 거부해도 일반 로그인이 가능합니다."],
  "Главная": ["Home","首页","홈"],
  "Ваши устройства, коллектив и персональный ассистент.": ["Your devices, collective and personal assistant.","您的设备、集体和个人助手。","내 기기, 커뮤니티 및 개인 어시스턴트."],
  "Вход по QR-коду": ["QR sign-in","二维码登录","QR 로그인"],
  "Создать QR-код": ["Create QR code","创建二维码","QR 코드 생성"],
  "Откройте безопасность аккаунта на другом авторизованном устройстве и отсканируйте код или вставьте ссылку.": ["Open account security on another signed-in device and scan the code or paste the link.","在另一台已登录设备上打开账户安全，扫描二维码或粘贴链接。","다른 로그인된 기기의 계정 보안에서 코드를 스캔하거나 링크를 붙여넣으세요."],
  "Код сравнения": ["Comparison code","比对码","확인 코드"],
  "Ожидаем подтверждение…": ["Waiting for approval…","等待确认…","승인 대기 중…"],
  "QR-вход недоступен. Используйте обычный вход.": ["QR sign-in is unavailable. Use regular sign-in.","二维码登录不可用，请使用普通登录。","QR 로그인을 사용할 수 없습니다. 일반 로그인을 이용하세요."],
  "Запрос истёк или отклонён. Создайте новый QR-код.": ["Request expired or was rejected. Create a new QR code.","请求已过期或被拒绝，请创建新二维码。","요청이 만료되었거나 거부되었습니다. 새 QR 코드를 생성하세요."],
  "Скопируйте ссылку запроса": ["Copy the request link","复制请求链接","요청 링크 복사"],
  "Вставьте ссылку или идентификатор запроса": ["Paste request link or ID","粘贴请求链接或 ID","요청 링크 또는 ID 붙여넣기"],
  "Проверить запрос": ["Review request","检查请求","요청 확인"],
  "Сканировать QR": ["Scan QR","扫描二维码","QR 스캔"],
  "Сканер недоступен. Вставьте ссылку вручную.": ["Scanner unavailable. Paste the link manually.","扫描器不可用，请手动粘贴链接。","스캐너를 사용할 수 없습니다. 링크를 직접 붙여넣으세요."],
  "Подтверждайте только свой запрос. Сравните код с экраном входа.": ["Approve only your own request. Compare the code with the sign-in screen.","仅确认自己的请求，请与登录界面对比代码。","본인의 요청만 승인하세요. 로그인 화면의 코드를 비교하세요."],
  "Введите код сравнения": ["Enter comparison code","输入比对码","확인 코드 입력"],
  "Разрешить вход": ["Allow sign-in","允许登录","로그인 허용"],
  "Вход подтверждён.": ["Sign-in approved.","登录已确认。","로그인이 승인되었습니다."],
  "Сведения о клиенте": ["Client details","客户端信息","클라이언트 정보"],
  "Мобильное устройство": ["Mobile device","移动设备","모바일 기기"],
  "Планшет": ["Tablet","平板电脑","태블릿"],
  "Компьютер": ["Computer","计算机","컴퓨터"],
  "Тип устройства определяется по данным клиента.": ["Device type is inferred from client information.","设备类型根据客户端信息推断。","기기 유형은 클라이언트 정보로 추정됩니다."],
  "Открыть MysticEgo Mobile": ["Open MysticEgo Mobile","打开 MysticEgo Mobile","MysticEgo Mobile 열기"],
  "Приложение для Android": ["Android app","Android 应用","Android 앱"],
  "Если приложение уже установлено, откройте его. Или продолжайте в браузере.": ["If the app is installed, open it. Or continue in your browser.","如果已安装应用，请打开它；也可以继续使用浏览器。","앱이 설치되어 있다면 열거나 브라우저에서 계속하세요."],
  "Открыть приложение": ["Open app","打开应用","앱 열기"],
  "Установка Android": ["Android installation","Android 安装","Android 설치"],
  "Продолжить в браузере": ["Continue in browser","继续使用浏览器","브라우저에서 계속"],
  "Android-клиент использует существующий личный кабинет и авторизацию MysticEgo.": ["The Android client uses the existing MysticEgo account and authentication.","Android 客户端使用现有 MysticEgo 账户与身份验证。","Android 클라이언트는 기존 MysticEgo 계정과 인증을 사용합니다."],
  "Публичная подписанная APK пока не выпущена. Проверяйте официальные релизы.": ["A signed public APK has not been released yet. Check official releases.","签名的公开 APK 尚未发布，请查看官方版本。","공개 서명 APK는 아직 출시되지 않았습니다. 공식 릴리스를 확인하세요."],
  "Официальные релизы": ["Official releases","官方版本","공식 릴리스"],
  "Язык": [
    "Language",
    "语言",
    "언어"
  ],
  "Шрифт": [
    "Font",
    "字体",
    "글꼴"
  ],
  "Современный": [
    "Modern",
    "现代",
    "모던"
  ],
  "Редакционный": [
    "Editorial",
    "典雅",
    "클래식"
  ],
  "Системный": [
    "System",
    "系统",
    "시스템"
  ],
  "Моноширинный": [
    "Monospace",
    "等宽",
    "고정폭"
  ],
  "Настройки": [
    "Settings",
    "设置",
    "설정"
  ],
  "Тема": [
    "Theme",
    "主题",
    "테마"
  ],
  "Тёмная": [
    "Dark",
    "深色",
    "어두운"
  ],
  "Светлая": [
    "Light",
    "浅色",
    "밝은"
  ],
  "Как в системе": [
    "System default",
    "跟随系统",
    "시스템 기본값"
  ],
  "Тема и интерфейс": [
    "Theme and interface",
    "主题与界面",
    "테마 및 인터페이스"
  ],
  "Тема, палитра и эффекты оформления": [
    "Theme, palette and visual effects",
    "主题、配色与视觉效果",
    "테마, 색상 및 시각 효과"
  ],
  "Сбросить оформление": [
    "Reset appearance",
    "重置外观",
    "외관 초기화"
  ],
  "Анимации": [
    "Animations",
    "动画",
    "애니메이션"
  ],
  "Обычные": [
    "Normal",
    "正常",
    "일반"
  ],
  "Минимальные": [
    "Minimal",
    "最少",
    "최소"
  ],
  "Выключены": [
    "Off",
    "关闭",
    "끄기"
  ],
  "Переходы": [
    "Transitions",
    "过渡",
    "전환"
  ],
  "Мягкое появление": [
    "Flow",
    "流畅出现",
    "부드럽게 나타나기"
  ],
  "Растворение": [
    "Fade",
    "淡入淡出",
    "페이드"
  ],
  "Скольжение": [
    "Slide",
    "滑动",
    "슬라이드"
  ],
  "Масштабирование": [
    "Scale",
    "缩放",
    "확대/축소"
  ],
  "Плотность": [
    "Density",
    "密度",
    "밀도"
  ],
  "Комфортная": [
    "Comfortable",
    "舒适",
    "편안함"
  ],
  "Компактная": [
    "Compact",
    "紧凑",
    "간결함"
  ],
  "Текст": [
    "Text",
    "文字",
    "텍스트"
  ],
  "Обычный": [
    "Normal",
    "正常",
    "일반"
  ],
  "Крупный": [
    "Large",
    "大号",
    "크게"
  ],
  "Насыщенный": [
    "Immersive",
    "沉浸式",
    "몰입형"
  ],
  "Упрощённый": [
    "Simplified",
    "简约",
    "간결형"
  ],
  "Пейзажи, глубина и мягкое свечение": [
    "Scenery, depth and soft glow",
    "风景、层次与柔光",
    "풍경, 깊이와 은은한 빛"
  ],
  "Чистые поверхности и спокойная сетка": [
    "Clean surfaces and a calm grid",
    "简洁表面与柔和网格",
    "깔끔한 표면과 차분한 격자"
  ],
  "Стиль интерфейса": [
    "Interface style",
    "界面风格",
    "인터페이스 스타일"
  ],
  "Насыщенный стиль": [
    "Immersive style",
    "沉浸式风格",
    "몰입형 스타일"
  ],
  "Тёмная тема": [
    "Dark theme",
    "深色主题",
    "어두운 테마"
  ],
  "Светлая тема": [
    "Light theme",
    "浅色主题",
    "밝은 테마"
  ],
  "Неоновые эффекты (насыщенный стиль)": [
    "Neon effects (immersive)",
    "霓虹效果（沉浸式）",
    "네온 효과 (몰입형)"
  ],
  "Анимация фонового свечения": [
    "Animated background glow",
    "动态背景光",
    "배경 발광 애니메이션"
  ],
  "Неоновая волна": [
    "Neon wave",
    "霓虹波",
    "네온 물결"
  ],
  "Градиент по затухающей сетке": [
    "Fading grid gradient",
    "渐隐网格渐变",
    "페이드 격자 그라데이션"
  ],
  "Свет по контуру кнопок": [
    "Button outline glow",
    "按钮轮廓光",
    "버튼 테두리 발광"
  ],
  "Яркость": [
    "Brightness",
    "亮度",
    "밝기"
  ],
  "Предпросмотр кнопки": [
    "Button preview",
    "按钮预览",
    "버튼 미리보기"
  ],
  "Палитра": [
    "Palette",
    "配色",
    "팔레트"
  ],
  "Своя палитра": [
    "Custom palette",
    "自定义配色",
    "사용자 지정 팔레트"
  ],
  "Основной цвет": [
    "Primary color",
    "主色",
    "기본 색상"
  ],
  "Второй цвет": [
    "Secondary color",
    "辅助色",
    "보조 색상"
  ],
  "Ирис": [
    "Iris",
    "鸢尾",
    "아이리스"
  ],
  "Нефрит": [
    "Jade",
    "翡翠",
    "제이드"
  ],
  "Янтарь": [
    "Amber",
    "琥珀",
    "앰버"
  ],
  "Арктика": [
    "Arctic",
    "极地",
    "아틱"
  ],
  "Роза": [
    "Rose",
    "玫瑰",
    "로즈"
  ],
  "Лайм": [
    "Lime",
    "青柠",
    "라임"
  ],
  "Индиго": [
    "Indigo",
    "靛蓝",
    "인디고"
  ],
  "Коралл": [
    "Coral",
    "珊瑚",
    "코랄"
  ],
  "Сталь": [
    "Steel",
    "钢蓝",
    "스틸"
  ],
  "Оркестр": [
    "Orchestra",
    "Orchestra",
    "Orchestra"
  ],
  "Управление экосистемой": [
    "Ecosystem management",
    "生态系统管理",
    "생태계 관리"
  ],
  "Управление организацией": [
    "Organization management",
    "组织管理",
    "조직 관리"
  ],
  "Управление площадкой": [
    "Site management",
    "场地管理",
    "사이트 관리"
  ],
  "Рабочая станция": [
    "Workstation",
    "工作站",
    "워크스테이션"
  ],
  "Личное пространство": [
    "Personal space",
    "个人空间",
    "개인 공간"
  ],
  "Рабочая область": [
    "Workspace",
    "工作区",
    "작업 공간"
  ],
  "Обзор": [
    "Overview",
    "概览",
    "개요"
  ],
  "Узлы сети": [
    "Network nodes",
    "网络节点",
    "네트워크 노드"
  ],
  "Быстрые команды": [
    "Quick commands",
    "快捷命令",
    "빠른 명령"
  ],
  "Автоматизация": [
    "Automation",
    "自动化",
    "자동화"
  ],
  "Комплект Agent": [
    "Agent package",
    "Agent 安装包",
    "Agent 패키지"
  ],
  "Развёртывание": [
    "Deployment",
    "部署",
    "배포"
  ],
  "Обновления Agent": [
    "Agent updates",
    "Agent 更新",
    "Agent 업데이트"
  ],
  "Организации": [
    "Organizations",
    "组织",
    "조직"
  ],
  "Команда разработки": [
    "Development team",
    "开发团队",
    "개발 팀"
  ],
  "Коллектив": [
    "Community",
    "社区",
    "커뮤니티"
  ],
  "Мой компьютер": [
    "My computer",
    "我的电脑",
    "내 컴퓨터"
  ],
  "Ассистент": [
    "Assistant",
    "助手",
    "어시스턴트"
  ],
  "Аватар VRM": [
    "VRM avatar",
    "VRM 形象",
    "VRM 아바타"
  ],
  "Тема VRM": [
    "VRM theme",
    "VRM 主题",
    "VRM 테마"
  ],
  "Система": [
    "System",
    "系统",
    "시스템"
  ],
  "Журнал действий": [
    "Activity log",
    "活动日志",
    "활동 로그"
  ],
  "Диагностика": [
    "Diagnostics",
    "诊断",
    "진단"
  ],
  "Ветки GitHub": [
    "GitHub branches",
    "GitHub 分支",
    "GitHub 브랜치"
  ],
  "Скачать CloudDesk": [
    "Download CloudDesk",
    "下载 CloudDesk",
    "CloudDesk 다운로드"
  ],
  "Инфраструктура": [
    "Infrastructure",
    "基础设施",
    "인프라"
  ],
  "Сервисы": [
    "Services",
    "服务",
    "서비스"
  ],
  "ИИ и речь": [
    "AI and speech",
    "AI 与语音",
    "AI 및 음성"
  ],
  "Базы данных": [
    "Databases",
    "数据库",
    "데이터베이스"
  ],
  "Метрики": [
    "Metrics",
    "指标",
    "지표"
  ],
  "Мой профиль": [
    "My profile",
    "我的个人资料",
    "내 프로필"
  ],
  "Аккаунт": [
    "Account",
    "账户",
    "계정"
  ],
  "Выйти": [
    "Sign out",
    "退出登录",
    "로그아웃"
  ],
  "Открыть меню": [
    "Open menu",
    "打开菜单",
    "메뉴 열기"
  ],
  "Закрыть меню": [
    "Close menu",
    "关闭菜单",
    "메뉴 닫기"
  ],
  "Главная навигация": [
    "Main navigation",
    "主导航",
    "주 탐색"
  ],
  "Навигация MysticEgo": [
    "MysticEgo navigation",
    "MysticEgo 导航",
    "MysticEgo 탐색"
  ],
  "Студия и команда": [
    "Studio and team",
    "工作室与团队",
    "스튜디오 및 팀"
  ],
  "Страница не найдена": [
    "Page not found",
    "页面未找到",
    "페이지를 찾을 수 없음"
  ],
  "К обзору": [
    "Back to overview",
    "返回概览",
    "개요로 돌아가기"
  ],
  "Проверяем доступ…": [
    "Checking access…",
    "正在检查权限…",
    "접근 권한 확인 중…"
  ],
  "Восстановление входа": [
    "Restoring sign-in",
    "恢复登录",
    "로그인 복원"
  ],
  "Повторить": [
    "Retry",
    "重试",
    "다시 시도"
  ],
  "Войти заново": [
    "Sign in again",
    "重新登录",
    "다시 로그인"
  ],
  "Гостевой сеанс": [
    "Guest session",
    "访客会话",
    "게스트 세션"
  ],
  "Создать аккаунт": [
    "Create account",
    "创建账户",
    "계정 만들기"
  ],
  "Войти в MysticEgo": [
    "Sign in to MysticEgo",
    "登录 MysticEgo",
    "MysticEgo 로그인"
  ],
  "Режим авторизации": [
    "Authentication mode",
    "登录方式",
    "인증 모드"
  ],
  "Вход": [
    "Sign in",
    "登录",
    "로그인"
  ],
  "Регистрация": [
    "Registration",
    "注册",
    "가입"
  ],
  "Логин": [
    "Username",
    "用户名",
    "사용자 이름"
  ],
  "Пароль": [
    "Password",
    "密码",
    "비밀번호"
  ],
  "Игровой никнейм": [
    "Game nickname",
    "游戏昵称",
    "게임 닉네임"
  ],
  "Скрыть пароль": [
    "Hide password",
    "隐藏密码",
    "비밀번호 숨기기"
  ],
  "Показать пароль": [
    "Show password",
    "显示密码",
    "비밀번호 표시"
  ],
  "Минимум 12 символов": [
    "At least 12 characters",
    "至少 12 个字符",
    "최소 12자"
  ],
  "Минимум 15 символов": ["At least 15 characters","至少 15 个字符","최소 15자"],
  "Принимаю": [
    "I accept the",
    "我接受",
    "동의합니다:"
  ],
  "условия использования": [
    "terms of use",
    "使用条款",
    "이용약관"
  ],
  "Как обрабатываются данные": [
    "How data is processed",
    "数据处理方式",
    "데이터 처리 방식"
  ],
  "Загружаем документы регистрации…": [
    "Loading registration documents…",
    "正在加载注册文件…",
    "가입 문서 불러오는 중…"
  ],
  "Подождите…": [
    "Please wait…",
    "请稍候…",
    "잠시만 기다려 주세요…"
  ],
  "Войти": [
    "Sign in",
    "登录",
    "로그인"
  ],
  "Адрес авторизации": [
    "Authentication address",
    "认证地址",
    "인증 주소"
  ],
  "API авторизации": [
    "Authentication API",
    "认证 API",
    "인증 API"
  ],
  "Войти в аккаунт": [
    "Sign in to account",
    "登录账户",
    "계정 로그인"
  ],
  "Временный аккаунт гостя": [
    "Temporary guest account",
    "临时访客账户",
    "임시 게스트 계정"
  ],
  "Панель организации": [
    "Organization panel",
    "组织面板",
    "조직 패널"
  ],
  "Панель клуба": [
    "Club panel",
    "俱乐部面板",
    "클럽 패널"
  ],
  "Рабочая станция клуба": [
    "Club workstation",
    "俱乐部工作站",
    "클럽 워크스테이션"
  ],
  "Личный профиль MysticEgo": [
    "MysticEgo personal profile",
    "MysticEgo 个人资料",
    "MysticEgo 개인 프로필"
  ],
  "Студия": [
    "Studio",
    "工作室",
    "스튜디오"
  ],
  "Разработчик": [
    "Developer",
    "开发者",
    "개발자"
  ],
  "Лицензия": [
    "License",
    "许可证",
    "라이선스"
  ],
  "Вход в панель": [
    "Sign in to panel",
    "登录面板",
    "패널 로그인"
  ],
  "Настройки не синхронизированы": [
    "Preferences not synchronized",
    "设置未同步",
    "설정이 동기화되지 않음"
  ],
  "Проверяем сохранённую сессию. Если сервер ещё запускается, вход восстановится автоматически.": [
    "Checking the saved session. Sign-in will resume automatically when the server is ready.",
    "正在检查已保存的会话。服务器就绪后将自动恢复登录。",
    "저장된 세션을 확인 중입니다. 서버가 준비되면 자동으로 로그인합니다."
  ],
  "Страницы MysticEgo": [
    "MysticEgo pages",
    "MysticEgo 页面",
    "MysticEgo 페이지"
  ],
  "Исходный код": [
    "Source code",
    "源代码",
    "소스 코드"
  ],
  "Лицензия кода": [
    "Code license",
    "代码许可证",
    "코드 라이선스"
  ],
  "Над чем работаем": [
    "Our projects",
    "我们的项目",
    "프로젝트"
  ],
  "Присоединиться к разработке": [
    "Join development",
    "参与开发",
    "개발 참여"
  ],
  "Партнёрство и финансирование": [
    "Partnership and funding",
    "合作与融资",
    "파트너십 및 자금 조달"
  ],
  "Проекты и опыт": [
    "Projects and experience",
    "项目与经验",
    "프로젝트 및 경험"
  ],
  "Технический стек": [
    "Technology stack",
    "技术栈",
    "기술 스택"
  ],
  "На связи": [
    "Get in touch",
    "联系我们",
    "연락처"
  ],
  "Идеи, которые хочу развивать": [
    "Ideas to develop",
    "未来发展构想",
    "발전시키고 싶은 아이디어"
  ],
  "Основные условия": [
    "Key terms",
    "主要条款",
    "주요 조건"
  ],
  "Коммерческое использование": [
    "Commercial use",
    "商业使用",
    "상업적 사용"
  ],
  "Полный оригинальный текст": [
    "Full original text",
    "完整原文",
    "전체 원문"
  ],
  "Содержание": [
    "Contents",
    "目录",
    "목차"
  ],
  "Читать лицензию": [
    "Read license",
    "阅读许可证",
    "라이선스 읽기"
  ],
  "Скачать текст": [
    "Download text",
    "下载文本",
    "텍스트 다운로드"
  ],
  "Запросить разрешение": [
    "Request permission",
    "申请许可",
    "권한 요청"
  ],
  "Условия сотрудничества": [
    "Commercial arrangements",
    "合作条款",
    "협력 조건"
  ],
  "Предыдущие версии и сторонние компоненты": [
    "Previous versions and third-party components",
    "旧版本与第三方组件",
    "이전 버전 및 타사 구성 요소"
  ],
  "MysticEgo / Правовая информация": [
    "MysticEgo / Legal information",
    "MysticEgo / 法律信息",
    "MysticEgo / 법률 정보"
  ],
  "Личное использование": [
    "Personal use",
    "个人使用",
    "개인 사용"
  ],
  "Изменение и распространение": [
    "Modification and distribution",
    "修改与分发",
    "수정 및 배포"
  ],
  "Студия VRM": [
    "VRM Studio",
    "VRM 工作室",
    "VRM 스튜디오"
  ],
  "Сцена": [
    "Scene",
    "场景",
    "장면"
  ],
  "Модель и движения": [
    "Model and motions",
    "模型与动作",
    "모델 및 동작"
  ],
  "Движение": [
    "Motion",
    "动作",
    "동작"
  ],
  "Сохранить аватар": [
    "Save avatar",
    "保存形象",
    "아바타 저장"
  ],
  "Чат с ассистентом": [
    "Assistant chat",
    "助手聊天",
    "어시스턴트 채팅"
  ],
  "На рабочий стол": [
    "Move to desktop",
    "移至桌面",
    "바탕 화면으로 이동"
  ],
  "Уведомления": [
    "Notifications",
    "通知",
    "알림"
  ],
  "Пользователи": [
    "Users",
    "用户",
    "사용자"
  ],
  "Оборудование": [
    "Hardware",
    "硬件",
    "하드웨어"
  ],
  "Программное обеспечение": [
    "Software",
    "软件",
    "소프트웨어"
  ],
  "Записи": [
    "Records",
    "记录",
    "기록"
  ],
  "Сохранить": [
    "Save",
    "保存",
    "저장"
  ],
  "Отмена": [
    "Cancel",
    "取消",
    "취소"
  ],
  "Обновить": [
    "Refresh",
    "刷新",
    "새로 고침"
  ],
  "Удалить": [
    "Delete",
    "删除",
    "삭제"
  ],
  "Добавить": [
    "Add",
    "添加",
    "추가"
  ],
  "Поиск": [
    "Search",
    "搜索",
    "검색"
  ],
  "permission.avatar-desktop": [
    "Desktop avatar",
    "桌面虚拟形象",
    "데스크톱 아바타",
    "Аватар на рабочем столе"
  ],
  "permission.browser-control": [
    "Browser control",
    "浏览器控制",
    "브라우저 제어",
    "Управление браузером"
  ],
  "permission.desktop-control": [
    "Desktop applications",
    "桌面应用控制",
    "데스크톱 앱 제어",
    "Управление приложениями"
  ],
  "permission.screen-capture": [
    "Screen capture",
    "屏幕捕获",
    "화면 캡처",
    "Захват экрана"
  ],
  "permission.recording": [
    "Record actions",
    "录制操作",
    "동작 기록",
    "Запись действий"
  ],
  "permission.replay": [
    "Replay scenarios",
    "重放场景",
    "시나리오 재생",
    "Воспроизведение сценариев"
  ],
  "permission.peripherals": [
    "Peripheral settings",
    "外设设置",
    "주변기기 설정",
    "Настройка периферии"
  ],
  "permission.vendor-updates": [
    "Driver and vendor updates",
    "驱动与厂商软件更新",
    "드라이버 및 제조사 업데이트",
    "Обновления драйверов и ПО"
  ],
  "permission.game-control": [
    "Game control",
    "游戏控制",
    "게임 제어",
    "Управление играми"
  ],
  "Безопасность аккаунта": [
    "Account security",
    "账户安全",
    "계정 보안"
  ],
  "Сессии, документы и разрешения устройств": [
    "Sessions, documents and device permissions",
    "会话、文件与设备权限",
    "세션, 문서 및 기기 권한"
  ],
  "Активные сессии": [
    "Active sessions",
    "活动会话",
    "활성 세션"
  ],
  "Сессия считается активной до выхода, отзыва или истечения срока. Последняя активность — время обновления токена.": [
    "A session remains active until sign-out, revocation or expiry. Last activity means last token refresh.",
    "会话在退出、撤销或到期前有效。最后活动时间为令牌刷新时间。",
    "로그아웃, 취소 또는 만료 전까지 세션이 유지됩니다. 마지막 활동은 토큰 갱신 시간입니다."
  ],
  "Текущая сессия": [
    "Current session",
    "当前会话",
    "현재 세션"
  ],
  "Старая сессия": [
    "Legacy session",
    "旧版会话",
    "이전 세션"
  ],
  "Другая сессия": [
    "Other session",
    "其他会话",
    "다른 세션"
  ],
  "Неизвестное устройство": [
    "Unknown device",
    "未知设备",
    "알 수 없는 기기"
  ],
  "Последняя активность": [
    "Last activity",
    "最后活动",
    "마지막 활동"
  ],
  "Создана": [
    "Created",
    "创建时间",
    "생성 시간"
  ],
  "Истекает": [
    "Expires",
    "到期时间",
    "만료 시간"
  ],
  "Завершить сессию": [
    "End session",
    "结束会话",
    "세션 종료"
  ],
  "Завершить все остальные сессии": [
    "End all other sessions",
    "结束其他所有会话",
    "다른 모든 세션 종료"
  ],
  "Все остальные устройства выйдут из аккаунта. Текущая сессия сохранится.": [
    "All other sessions will be signed out. This session will remain active.",
    "其他所有会话将退出登录。当前会话将保留。",
    "다른 모든 세션이 로그아웃됩니다. 현재 세션은 유지됩니다."
  ],
  "Подтвердите завершение выбранной сессии.": [
    "Confirm ending the selected session.",
    "确认结束选定会话。",
    "선택한 세션의 종료를 확인하세요."
  ],
  "Подтвердить": [
    "Confirm",
    "确认",
    "확인"
  ],
  "Документы и разрешения аккаунта": [
    "Account documents and permissions",
    "账户文件与权限",
    "계정 문서 및 권한"
  ],
  "Актуальные документы приняты.": [
    "Current documents accepted.",
    "已接受当前文件。",
    "최신 문서에 동의했습니다."
  ],
  "Необходимо ознакомиться с актуальными документами.": [
    "Review the current documents.",
    "请查阅当前文件。",
    "최신 문서를 확인하세요."
  ],
  "Подтверждаю ознакомление с условиями, политикой обработки данных и лицензией.": [
    "I accept the terms, privacy notice and license.",
    "我接受使用条款、隐私声明和许可证。",
    "이용약관, 개인정보 처리 안내 및 라이선스에 동의합니다."
  ],
  "Выберите возможности. Разрешение конкретного устройства потребуется отдельно.": [
    "Choose optional features. Each device requires separate approval.",
    "选择可选功能。每台设备需要单独授权。",
    "선택 기능을 설정하세요. 각 기기에는 별도 승인이 필요합니다."
  ],
  "Разрешения ассистента": [
    "Assistant permissions",
    "助手权限",
    "어시스턴트 권한"
  ],
  "Разрешения устройств": [
    "Device permissions",
    "设备权限",
    "기기 권한"
  ],
  "Отозвать разрешение": [
    "Revoke permission",
    "撤销权限",
    "권한 취소"
  ],
  "Нет подтверждённых разрешений устройств.": [
    "No approved device permissions.",
    "没有已批准的设备权限。",
    "승인된 기기 권한이 없습니다."
  ],
  "Условия": [
    "Terms",
    "条款",
    "약관"
  ],
  "Обработка данных": [
    "Privacy notice",
    "隐私声明",
    "개인정보 처리 안내"
  ],
  "Настройки приватности": ["Privacy settings","隐私设置","개인정보 설정"],
  "Для входа и сохранения настроек используются необходимые данные браузера. Рекламные и аналитические cookies не используются.": ["Essential browser storage is used for sign-in and preferences. Advertising and analytics cookies are not used.","登录和保存设置仅使用必要的浏览器数据。不使用广告或分析 Cookie。","로그인과 설정 저장에 필수 브라우저 데이터만 사용합니다. 광고 및 분석 쿠키는 사용하지 않습니다."],
  "Разрешить проверку подключённых приложений MysticEgo на этом устройстве. Возможен системный запрос доступа к локальной сети.": ["Allow discovery of MysticEgo apps on this device. Your system may request local network access.","允许发现此设备上的 MysticEgo 应用。系统可能请求访问本地网络。","이 기기에서 MysticEgo 앱을 검색하도록 허용합니다. 시스템이 로컬 네트워크 접근 권한을 요청할 수 있습니다."],
  "Только необходимые": ["Essential only","仅必要项","필수 항목만"],
  "Сохранить настройки": ["Save preferences","保存设置","설정 저장"],
  "Браузер не поддерживается": ["Unsupported browser","浏览器不受支持","지원하지 않는 브라우저"],
  "Вход доступен в Mozilla Firefox, Яндекс Браузере, Microsoft Edge и Google Chrome.": ["Sign-in is supported in Firefox, Yandex Browser, Edge and Chrome.","支持使用 Firefox、Yandex Browser、Edge 和 Chrome 登录。","Firefox, Yandex Browser, Edge, Chrome에서 로그인할 수 있습니다."],
  "Откройте этот адрес в одном из поддерживаемых браузеров.": ["Open this address in a supported browser.","请使用受支持的浏览器打开此地址。","지원하는 브라우저에서 이 주소를 열어 주세요."],
  "О проекте MysticEgo": ["About MysticEgo","关于 MysticEgo","MysticEgo 소개"],
  "Обновления MysticEgo": ["MysticEgo updates","MysticEgo 更新","MysticEgo 업데이트"],
  "Доступна новая версия": ["New version available","新版本已就绪","새 버전 사용 가능"],
  "Сохраните текущую работу и обновите страницу, чтобы открыть новую версию.": ["Save your work and reload the page to use the new version.","请保存当前工作并刷新页面以使用新版本。","작업을 저장한 후 페이지를 새로 고쳐 새 버전을 사용하세요."],
  "Обновить страницу": ["Reload page","刷新页面","페이지 새로 고침"],
  "Что нового": ["What's new","版本亮点","새로운 기능"],
  "Закрыть": ["Close","关闭","닫기"],
  "MysticEgo стал лучше. Узнайте, что изменилось в этой версии.": ["Discover the improvements in this MysticEgo release.","探索 MysticEgo 本次版本更新的亮点。","이번 MysticEgo 업데이트의 개선 사항을 확인하세요."],
  "Изменения версии": ["Release notes","版本说明","릴리스 노트"],
  "Описание релиза доступно на русском языке.": ["The release notes are currently available in Russian.","版本说明目前仅提供俄语。","현재 릴리스 노트는 러시아어로 제공됩니다."],
  "Спасибо, что используете MysticEgo.": ["Thank you for using MysticEgo.","感谢使用 MysticEgo。","MysticEgo를 이용해 주셔서 감사합니다."],
  "Ознакомился": ["Got it","了解了","확인"],
  "Меню аккаунта": ["Account menu","账户菜单","계정 메뉴"],
  "Бизнес-граф": ["Business topology","业务拓扑","비즈니스 구조"],
  "Development": ["Development","开发","개발","Разработка"],
  "Tasks & discussions": ["Tasks & discussions","任务与讨论","작업 및 토론","Задачи и обсуждения"],
  "AI и речь": ["AI and speech","AI 与语音","AI 및 음성"],
  "Мониторинг": ["Monitoring","监控","모니터링"],
  "Макро контроль": ["Infrastructure overview","全局监控","전체 인프라 모니터링"],
  "Микро контроль": ["Service detail","服务监控","서비스 상세 모니터링"],
  "Автотесты": ["Self-tests","自检","자가 진단"],
  "Capacity & resilience": ["Capacity & resilience","容量与韧性","용량 및 복원력","Мощность и отказоустойчивость"],
  "Development team access required.": ["Development team access required.","仅开发团队可访问。","개발 팀 권한이 필요합니다.","Требуется доступ команды разработки."],
  "Скачивание CloudDesk доступно команде разработки.": ["CloudDesk downloads are available to the development team.","CloudDesk 下载仅对开发团队开放。","CloudDesk 다운로드는 개발 팀만 이용할 수 있습니다."],
  "Оркестр MysticEgo": ["MysticEgo Orchestra","MysticEgo Orchestra","MysticEgo Orchestra"],
  "Обзор организации": ["Organization overview","组织概览","조직 개요"],
  "Обзор площадки": ["Site overview","场地概览","사이트 개요"],
  "Организации, площадки и состояние сети в едином пространстве.": ["Organizations, sites and network status in one workspace.","集中查看组织、场地和网络状态。","한 작업 공간에서 조직, 사이트 및 네트워크 상태를 확인합니다."],
  "Доступные организации и площадки сети MysticEgo.": ["Accessible organizations and sites in the MysticEgo network.","MysticEgo 网络中的可用组织与场地。","MysticEgo 네트워크에서 접근 가능한 조직 및 사이트."],
  "Топология сети": ["Network topology","网络拓扑","네트워크 구조"],
  "Состояние организаций": ["Organization status","组织状态","조직 상태"],
  "Участники и площадки": ["Members and sites","成员与场地","구성원 및 사이트"],
  "CPU Оркестра · 1 мин": ["Orchestra CPU · 1 min","Orchestra CPU · 1 分钟","Orchestra CPU · 1분"],
  "Граф бизнес-логики": ["Business topology","业务拓扑","비즈니스 구조"],
  "Организации, площадки, рабочие станции и их принадлежность": ["Organizations, sites, workstations and their relationships","组织、场地、工作站及其归属关系","조직, 사이트, 워크스테이션 및 소속 관계"],
  "Топология": ["Topology","拓扑","구조"],
  "Выберите узел": ["Select a node","选择节点","노드 선택"],
  "Рабочая станция MysticEgo": ["MysticEgo workstation","MysticEgo 工作站","MysticEgo 워크스테이션"],
  "Мониторинг и управление доступными устройствами и командами.": ["Monitor and manage available devices and commands.","监控和管理可用设备与命令。","사용 가능한 기기와 명령을 모니터링하고 관리합니다."],
  "Состояние узлов": ["Node status","节点状态","노드 상태"],
  "Узлы пока не зарегистрированы": ["No registered nodes yet","尚未注册节点","등록된 노드가 없습니다"],
  "Активные агенты": ["Active agents","活跃代理","활성 에이전트"],
  "Агенты не подключены": ["No connected agents","尚无代理连接","연결된 에이전트가 없습니다"],
  "Известен": ["Registered","已注册","등록됨"],
  "Онлайн": ["Online","在线","온라인"],
  "Офлайн": ["Offline","离线","오프라인"],
  "Релизы и ветки исходного кода MysticEgo": ["MysticEgo releases and source branches","MysticEgo 版本与源代码分支","MysticEgo 릴리스 및 소스 브랜치"],
  "Автоматическое обслуживание": ["Automatic maintenance","自动维护","자동 유지보수"],
  "Вычислительный контур": ["Compute environment","计算环境","컴퓨팅 환경"],
  "Безопасность данных": ["Data protection","数据保护","데이터 보호"],
  "Просмотр диагностики": ["View diagnostics","查看诊断","진단 보기"],
  "ОБЗОР": ["OVERVIEW","概览","개요"],
  "Открыть": ["Open","打开","열기"],
  "Схема сети": ["Network map","网络图","네트워크 지도"],
  "Развернуть Global": ["Deploy Global","部署 Global","Global 배포"],
  "Организации Global": ["Global organizations","Global 组织","Global 조직"],
  "Участники": ["Members","成员","구성원"],
  "Площадки Local": ["Local sites","Local 场地","Local 사이트"],
  "Global в сети": ["Global online","在线 Global","온라인 Global"],
  "Обновление недоступно": ["Refresh unavailable","无法刷新","새로 고침 불가"],
  "Топология получена": ["Topology loaded","拓扑已加载","네트워크 구조 로드됨"],
  "Загрузка топологии…": ["Loading topology…","正在加载拓扑…","네트워크 구조 불러오는 중…"],
  "Топология недоступна": ["Topology unavailable","拓扑不可用","네트워크 구조를 사용할 수 없음"],
  "Загружаем топологию…": ["Loading topology…","正在加载拓扑…","네트워크 구조 불러오는 중…"],
  "Нет организаций": ["No organizations","暂无组织","조직 없음"],
  "Данные недоступны": ["Data unavailable","数据不可用","데이터 없음"],
  "Данные организаций недоступны": ["Organization data unavailable","组织数据不可用","조직 데이터 없음"],
  "Загружаем организации…": ["Loading organizations…","正在加载组织…","조직 불러오는 중…"],
  "Здесь появятся созданные Global": ["Created Global nodes will appear here","已创建的 Global 节点将显示在此处","생성된 Global 노드가 여기에 표시됩니다"],
  "Подготовить первую организацию": ["Set up your first organization","设置首个组织","첫 조직 설정"],
  "Название": ["Name","名称","이름"],
  "Состояние": ["Status","状态","상태"],
  "Люди": ["People","人员","인원"],
  "Нет доступных данных": ["No data available","暂无数据","사용 가능한 데이터 없음"],
  "Диаграмма появится после создания Global.": ["The chart appears after a Global node is created.","创建 Global 节点后将显示图表。","Global 노드를 생성하면 차트가 표시됩니다."],
  "Открыть схему сети": ["Open network map","打开网络图","네트워크 지도 열기"],
  "Показаны последние полученные данные; обновление не удалось.": ["Showing last received data; refresh failed.","显示上次获取的数据；刷新失败。","마지막으로 수신한 데이터를 표시합니다. 새로 고침에 실패했습니다."],
  "Отключён": ["Disabled","已禁用","비활성화"],
  "В сети": ["Online","在线","온라인"],
  "Нет связи": ["Offline","离线","오프라인"],
  "Нет отчёта": ["No report","暂无报告","보고서 없음"],
  "Быстрая команда": ["Quick command","快捷命令","빠른 명령"],
  "Выполнение на агенте": ["Run on agent","在代理上执行","에이전트에서 실행"],
  "Мои пресеты": ["My presets","我的预设","내 프리셋"],
  "Частые операции": ["Frequent actions","常用操作","자주 쓰는 작업"],
  "Тест связи": ["Connection test","连接测试","연결 테스트"],
  "HTTP, SignalR и скорость": ["HTTP, SignalR and speed","HTTP、SignalR 与速度","HTTP, SignalR 및 속도"],
  "Все агенты": ["All agents","所有代理","모든 에이전트"],
  "Проверяется": ["Checking","正在检查","확인 중"],
  "Готово": ["Ready","就绪","준비 완료"],
  "Восстановление": ["Recovering","正在恢复","복구 중"],
  "Требует внимания": ["Needs attention","需要关注","확인 필요"],
  "Заданная конфигурация": ["Configured externally","外部配置","외부 설정"],
  "Отключено": ["Disabled","已禁用","비활성화"],
  "Служба обслуживания Orchestra": ["Orchestra maintenance service","Orchestra 维护服务","Orchestra 유지보수 서비스"],
  "Распознавание речи": ["Speech recognition","语音识别","음성 인식"],
  "Служба PostgreSQL": ["PostgreSQL service","PostgreSQL 服务","PostgreSQL 서비스"],
  "Текущая база": ["Current database","当前数据库","현재 데이터베이스"],
  "Пока нет подтверждённого замера этого компонента.": ["No verified measurement for this component yet.","此组件暂无已确认的测量数据。","이 구성 요소의 확인된 측정값이 아직 없습니다."],
  "Проверено": ["Checked","已检查","확인 시간"],
  "Ожидание отчёта сервера": ["Waiting for server report","等待服务器报告","서버 보고서 대기 중"],
  "Разделы инфраструктуры": ["Infrastructure sections","基础设施分类","인프라 섹션"],
  "Локальные системные службы, Docker и механизм самостоятельного обслуживания.": ["Local system services, Docker and self-maintenance.","本地系统服务、Docker 与自动维护。","로컬 시스템 서비스, Docker 및 자동 유지보수."],
  "Вычислительные модели, LM Studio и локальное распознавание голосовых сообщений.": ["Compute models, LM Studio and local voice transcription.","计算模型、LM Studio 与本地语音转写。","연산 모델, LM Studio 및 로컬 음성 전사."],
  "Текущее хранилище, PostgreSQL и подготовка безопасной миграции.": ["Current storage, PostgreSQL and safe migration preparation.","当前存储、PostgreSQL 与安全迁移准备。","현재 저장소, PostgreSQL 및 안전한 마이그레이션 준비."],
  "Источники метрик, Prometheus и Grafana. Настройка сбора телеметрии отдельно от диагностики.": ["Metrics sources, Prometheus and Grafana. Configure telemetry collection separately from diagnostics.","指标来源、Prometheus 与 Grafana。遥测采集配置独立于诊断。","메트릭 소스, Prometheus 및 Grafana. 진단과 별도로 원격 측정 수집을 설정합니다."],
  "Установка Docker и Compose": ["Install Docker and Compose","安装 Docker 与 Compose","Docker 및 Compose 설치"],
  "Развёртывание и подключение моделей": ["Deploy and connect models","部署与连接模型","모델 배포 및 연결"],
  "Установка и подключение PostgreSQL": ["Install and connect PostgreSQL","安装与连接 PostgreSQL","PostgreSQL 설치 및 연결"],
  "Настройка сбора метрик": ["Configure metrics collection","配置指标采集","메트릭 수집 설정"],
  "Проверить": ["Check","检查","확인"],
  "Служба обслуживания передаёт данные": ["Maintenance service is reporting","维护服务正在报告","유지보수 서비스 보고 중"],
  "Служба требует внимания": ["Service needs attention","服务需要关注","서비스 확인 필요"],
  "Состояние службы обслуживания": ["Maintenance service status","维护服务状态","유지보수 서비스 상태"],
  "Скачать отчёт": ["Download report","下载报告","보고서 다운로드"],
  "Формат отчёта": ["Report format","报告格式","보고서 형식"],
  "Установлено": ["Installed","已安装","설치됨"],
  "Версии MysticEgo": ["MysticEgo versions","MysticEgo 版本","MysticEgo 버전"],
  "не указано": ["not specified","未指定","지정되지 않음"],
  "недоступен": ["unavailable","不可用","사용 불가"],
  "данные устарели": ["data is stale","数据已过期","오래된 데이터"],
  "предварительный": ["prerelease","预发布版","사전 릴리스"],
  "Скрыть уведомление": ["Dismiss notification","隐藏通知","알림 숨기기"],
  "Закрыть уведомления": ["Close notifications","关闭通知","알림 닫기"],
  "Новых уведомлений нет.": ["No new notifications.","没有新通知。","새 알림이 없습니다."],
  "Отметить прочитанным": ["Mark as read","标为已读","읽음으로 표시"],
  "непрочитанных": ["unread","条未读","개 읽지 않음"],
  "Браузер": ["Browser","浏览器","브라우저"],
  "Личное пространство для диалогов, действий и помощи с компьютером.": ["Your personal workspace for conversations, actions and computer assistance.","您的个人对话、操作与电脑助手空间。","대화, 작업, 컴퓨터 지원을 위한 개인 공간."],
  "Разделы ассистента": ["Assistant sections","助手分类","어시스턴트 섹션"],
  "Чат": ["Chat","聊天","채팅"],
  "Модели": ["Models","模型","모델"],
  "Характер": ["Persona","角色设定","캐릭터"],
  "Разработка": ["Development","开发","개발"],
  "Проверяем подключение…": ["Checking connection…","正在检查连接…","연결 확인 중…"],
  "Модель недоступна": ["Model unavailable","模型不可用","모델을 사용할 수 없음"],
  "Личный чат": ["Personal chat","个人聊天","개인 채팅"],
  "Рабочее пространство": ["Workspace","工作空间","작업 공간"],
  "Характер ассистента": ["Assistant persona","助手角色设定","어시스턴트 캐릭터"],
  "Настройте имя, стиль общения и голос.": ["Choose a name, conversation style and voice.","设置名称、交流风格与声音。","이름, 대화 스타일 및 음성을 설정하세요."],
  "Настроить": ["Configure","设置","설정"],
  "Голос": ["Voice","语音","음성"],
  "Включено": ["Enabled","已启用","활성화"],
  "Выключено": ["Disabled","已关闭","비활성화"],
  "Озвучивание ответов на вашем устройстве.": ["Spoken replies on your device.","在您的设备上朗读回复。","기기에서 답변을 음성으로 읽습니다."],
  "Модель, движения и рабочий стол.": ["Model, motions and desktop.","模型、动作与桌面。","모델, 동작 및 데스크톱."],
  "Создавайте и запускайте разрешённые сценарии.": ["Create and run approved workflows.","创建并运行已授权的工作流。","승인된 워크플로를 만들고 실행하세요."],
  "Управление моделями": ["Manage models","模型管理","모델 관리"]
};
