# Духовная связь / Spiritual Connection

Связано с [Epic #118](https://github.com/ZeroFantom/MysticEgo/issues/118). Статус: доменная политика, однопроцессный реестр снимков присутствия и smoke-проверки; не подключено к API, мессенджерам или игровому миру.

## Правила мира

- NPC воспринимает сообщение как духовную связь с игровой сущностью, не как Telegram/VK или работу ИИ. Профили соцсетей, токены и технические метаданные не входят в игровой контекст.
- NPC и игроки без подтверждённой дружбы доступны для общения только в общей текущей игровой области. Другие игроки с подтверждённой дружбой могут общаться в разных областях.
- При перемещении и отзыве дружбы доступ пересчитывается; сохранённая история не является разрешением отправить новое сообщение. Инициатива NPC подчиняется тому же правилу.
- Для первой версии консервативно запрещены связь с NPC вне области, межмировая связь, неизвестная область и NPC↔NPC через этот пользовательский шлюз. Это не запрет внутренних NPC-сцен мира. Возможность связи с NPC-другом, между мирами, с душой без воплощения и точные границы области требуют решения владельца.
- Онлайн-статус не равен местоположению. Офлайн-игрок может иметь серверно подтверждённое размещение. Если такого размещения нет, доступ закрыт.

## Интеграция

1. Auth разрешает проверенный внешний идентификатор в существующий UserId. Отдельно проверяется право на активную игровую сущность; привязка соцсети не даёт право управлять персонажем другого игрока.
2. Авторитетный игровой сервис возвращает согласованный снимок обоих участников (EntityId, WorldId, AreaId, Kind). Для исключения требуется подтверждённая дружба именно этой пары; существующие контакты Collective хранятся на уровне аккаунта, поэтому требуется явное сопоставление. Не принимать эти поля и признак дружбы как утверждение клиента.
3. SpiritualConnectionPolicy.Evaluate возвращает Local, Friend либо Unavailable. Внешний ответ о недоступности должен быть одинаковым для скрытой, неизвестной и запрещённой цели. Политика не является полноценной авторизацией; дополнительно проверяются права, блокировки, согласия и состояние мира.
4. Один прикладной сервис проверяет правила при поиске, отправке, постановке в очередь и повторно перед доставкой/изменением мира. Проверка и запись события должны использовать согласованную версию состояния или транзакцию, чтобы перемещение/отзыв дружбы не создавали окно обхода.
5. Адаптеры Telegram/VK/мобильного клиента не содержат отдельные игровые правила. Общая история, дедупликация, очередь и outbox должны переживать перезапуск. Для NPC формируется контекст игровой личности, памяти и духовного способа общения без транспортных метаданных.
6. Существующие обычные чаты Collective пока не меняются; подключение к новой политике требует отличать внутриигровой чат от аккаунтного общения. Не выдавать публичный API до появления доверенного источника состояния.

## Проверки

`dotnet run --project tests/CollectiveHttpSmoke/CollectiveHttpSmoke.csproj -c Release`

Smoke запускает доменную матрицу отдельно от HTTP. Она проверяет локальность, дружбу, перемещение, потерю дружбы, отсутствующие/невалидные размещения, изоляцию миров, инициативу NPC и запрет обхода NPC через признак дружбы. Это не доказательство готовности транспорта или распределённой доставки.

## Прототип World Presence

`WorldPresenceRegistry` принимает полный снимок от доверенного серверного писателя, а не перемещение из HTTP-запроса. `TryReplace` проверяет уникальность идентификаторов, тип сущности, принадлежность игрока аккаунту и версию ожидаемого снимка. Замена атомарна внутри одного процесса; удаление сущности из нового снимка делает её недоступной. При перезапуске реестр пуст, новая случайная версия не совпадает с предыдущей. `ResolvePlayerPair` читает пару согласованно и проверяет принадлежность отправителя аутентифицированному аккаунту. `RecipientAccountId` нужен исключительно серверному сопоставлению дружбы, не NPC и не клиентскому ответу.

Открытые ограничения: нет источника реальных перемещений, сохранения снимков, межузлового fencing, каталога областей, разрешения активного воплощения и атомарной записи сообщений. `IsCurrent` — только проверка свежести, не транзакционная гарантия. Для NPC-инициативы нужен отдельный доверенный игровой путь, а не подмена игрока. Не регистрировать этот прототип как production-хранилище. Существующая система Collective остаётся неизменной. Следующий этап — интеграционный контракт с настоящим сервером мира, хранилище/версионирование и безопасная проверка дружбы.

## Mobile, Spatial AR and Avatar Handoff
Parallel PR [#117](https://github.com/ZeroFantom/MysticEgo/pull/117) contains WebXR tracking/anchors and VRM handoff. A real-room anchor or avatar animation is presentation state, never authoritative AreaId/WorldId or proof of an active embodiment. Moving VRM in AR cannot bypass spiritual reachability; changing a game area does not move a physical anchor. Handoff carries only approved presentation state; conversation history is account/world-scoped and retrieved under fresh authorization. The personal assistant can use separate account-level context; an NPC communication remains governed by world presence. Keep camera frames, room geometry, social IDs and external credentials out of NPC prompts. Verify phone behavior separately from CI.

## Communication Gateway — reference application service
SpiritualCommunicationGateway is a bounded single-process reference implementation. A trusted world publisher atomically supplies placements, one active embodiment per account, exact directional communication permissions (absence denies), and confirmed mutual player-entity friendships. The caller supplies an independently authenticated account, a request ID, target entity and text only. The gateway resolves the active sender internally; it never accepts client area, sender or friendship assertions. It calls the same SpiritualConnectionPolicy at discovery/reachability, enqueue and commit to the local world inbox. Movement, revocation of permission/friendship and embodiment changes between acceptance and commit deny delivery. A committed in-world envelope contains entity/world IDs, message text, time and spiritual_connection medium only; transport metadata is excluded. Existing Collective account chats are unchanged.

This is NOT persistent messaging or an HTTP endpoint. A single lock gives local atomicity, not cross-node authority. Bounded queue (1024) and dedup records (8192) fail closed at capacity; no automatic eviction or restart recovery. Same account/request ID and content deduplicate acceptance, including previously discarded attempts; acceptance is not delivery status. Committed is a trusted observation for tests, not a public history read. Do not use it to authorize network delivery. Production needs a durable inbox/outbox with delivery status and retention, transactional version/permission checks, authenticated external identity and active-world adapters, block/consent policy, provider revalidation before delivery, and NPC initiative path. Social account friendship must be explicitly resolved to exact active game entities. No assumption that a social login subject equals a bot user ID. Smoke tests cover ownership, locality, friendship, permission, replay, stale state and movement at delivery.
