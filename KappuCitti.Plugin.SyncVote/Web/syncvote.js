// SyncVote Plugin JavaScript for Jellyfin Web Interface

// Minimal i18n helper + alert fallback
// Detect plugin context (pluginId from script src) and resource URL builder
(function bootstrapSyncVoteGlobals() {
    try {
        const s = document.currentScript;
        const url = new URL(s && s.src ? s.src : window.location.href, window.location.origin);
        const pid = url.searchParams.get("pluginId");
        const SV = (window.SyncVote = window.SyncVote || {});
        // PluginResources expects pluginId without dashes
        SV.pluginId = (pid && pid.trim()) || SV.pluginId || "6bfde2dd82114964b86d7812a9de160b";
        SV.resUrl = function (name) {
            try {
                if (window.ApiClient && typeof window.ApiClient.getUrl === "function") {
                    return window.ApiClient.getUrl("web/PluginResources", { pluginId: SV.pluginId, name: name });
                }
            } catch (_) {}
            return "/web/PluginResources?pluginId=" + encodeURIComponent(SV.pluginId) + "&name=" + encodeURIComponent(name);
        };
    } catch (_) {}
})();
class SVI18n {
    constructor() {
        this.lang = "en";
        this.dict = {};
    }
    async init() {
        try {
            const nav = (navigator && (navigator.language || (navigator.languages && navigator.languages[0]))) || "en";
            const base = (nav || "en").toLowerCase().split("-")[0];
            this.lang = base;
            for (const l of [base, "en"]) {
                try {
                    const url = window.SyncVote && window.SyncVote.resUrl ? window.SyncVote.resUrl(`i18n/${l}.json`) : `i18n/${l}.json`;
                    const res = await fetch(url, { cache: "no-store" });
                    if (res.ok) {
                        this.dict = await res.json();
                        return;
                    }
                } catch (_) {
                    /* ignore */
                }
            }
        } catch (_) {
            /* ignore */
        }
    }
    t(key, ...args) {
        const v = (this.dict && this.dict[key]) || key;
        if (!args || !args.length) return v;
        return v.replace(/\{(\d+)\}/g, (m, i) => (typeof args[i] !== "undefined" ? args[i] : m));
    }
}

function svAlert(messageOrOptions) {
    try {
        if (typeof Dashboard !== "undefined" && typeof Dashboard.alert === "function") return Dashboard.alert(messageOrOptions);
    } catch (_) {}
    if (typeof messageOrOptions === "string") {
        alert(messageOrOptions);
        return Promise.resolve();
    }
    if (messageOrOptions && messageOrOptions.text) {
        alert(messageOrOptions.text);
    }
    return Promise.resolve();
}

class SyncVoteManager {
    constructor() {
        console.log("[SyncVote] Initializing SyncVoteManager");
        this.apiClient = window.ApiClient;
        this.currentRoom = null;
        this.votingTimer = null;
        this._candidates = [];
        this._candidateIndex = 0;
        this.votesCount = 0;
        this._menuObserver = null;
        this.i18n = new SVI18n();
        this.init();
    }

    init() {
        console.log("[SyncVote] Running init(), ApiClient available:", !!this.apiClient);
        // Try to attach controls into SyncPlay UI
        this.addSyncVoteButton();
        // Also attempt once on load, in case already joined
        this.getCurrentSyncPlayGroupId().then((groupId) => {
            console.log("[SyncVote] Current SyncPlay group:", groupId);
            if (groupId) this.updateSyncVoteControls(groupId);
        });

        // Observe action sheet openings to inject our menu items under SyncPlay group menu
        this._observeSyncPlayMenus();
        // Load translations
        this.i18n.init();
    }

    addSyncVoteButton() {
        // Ensure we have a container; actual buttons are managed by updateSyncVoteControls
        const syncPlayContainer = document.querySelector(".syncPlayContainer");
        if (syncPlayContainer) {
            // Nothing to do here; we'll render context-aware controls later
        }
    }

    async onSyncPlayGroupJoined(groupInfo) {
        // Check if there's already a voting room for this SyncPlay group
        try {
            const rooms = await this.apiClient.ajax({
                type: "GET",
                url: this.apiClient.getUrl("SyncVote/Rooms"),
            });

            const existingRoom = rooms.find((r) => r.SyncPlayGroupId === groupInfo.GroupId);
            if (existingRoom) {
                this.currentRoom = existingRoom;
            }
            await this.updateSyncVoteControls(groupInfo.GroupId);
        } catch (error) {
            console.error("Error checking for existing voting rooms:", error);
        }
    }

    _observeSyncPlayMenus() {
        if (this._menuObserver) return;
        const target = document.body;
        if (!target) return;
        console.log("[SyncVote] Starting SyncPlay menu observer");
        this._menuObserver = new MutationObserver(async () => {
            try {
                // Try multiple selectors for different Jellyfin Web versions
                // .opened class may not always be present, also try without it
                const sheets = document.querySelectorAll(".actionSheet.syncPlayGroupMenu.opened, .actionSheet.syncPlayGroupMenu, .dialogContainer .syncPlayGroupMenu");
                for (const sheet of sheets) {
                    // Skip if not visible
                    if (sheet.offsetParent === null && !sheet.classList.contains("opened")) continue;
                    const scroller = sheet.querySelector(".actionSheetScroller, .scrollerContainer, .dialogContent");
                    if (!scroller || scroller.querySelector(".svSyncVoteItem")) continue;
                    console.log("[SyncVote] Found SyncPlay menu, injecting controls");

                    // Resolve current group and room/owner
                    const groupId = await this.getCurrentSyncPlayGroupId();
                    if (!groupId) continue;
                    const rooms = await this.apiClient.ajax({ type: "GET", url: this.apiClient.getUrl("SyncVote/Rooms") }).catch(() => []);
                    const room = (rooms || []).find((r) => r.SyncPlayGroupId === groupId) || null;
                    this.currentRoom = room;
                    const userId = await this.apiClient.getCurrentUserId();
                    const isOwner = !!room && room.OrganizerId && room.OrganizerId.toLowerCase() === (userId || "").toLowerCase();
                    const perms = await this.apiClient.ajax({ type: "GET", url: this.apiClient.getUrl("SyncVote/Permissions") }).catch(() => ({ CanOrganize: true }));

                    // Build item(s)
                    if (!room) {
                        this._appendActionItem(scroller, {
                            id: "sv-create",
                            icon: "how_to_vote",
                            title: this.i18n.t("createVoting"),
                            subtitle: this.i18n.t("createVoting.subtitle"),
                            onClick: () => this.showVotingDialog(),
                        });
                    }

                    if (room && isOwner) {
                        this._appendActionItem(scroller, {
                            id: "sv-manage",
                            icon: "how_to_vote",
                            title: room.IsVotingActive ? this.i18n.t("manageVoting.manage") : this.i18n.t("manageVoting.start"),
                            subtitle: room.IsVotingActive ? this.i18n.t("manageVoting.subtitle.manage") : this.i18n.t("manageVoting.subtitle.start"),
                            onClick: () => this.showVotingInterface(),
                        });
                        this._appendActionItem(scroller, {
                            id: "sv-results",
                            icon: "emoji_events",
                            title: this.i18n.t("results.show"),
                            subtitle: this.i18n.t("results.subtitle"),
                            onClick: () => this.showCurrentResults?.(),
                        });
                        this._appendActionItem(scroller, {
                            id: "sv-settings",
                            icon: "settings",
                            title: this.i18n.t("config.title"),
                            subtitle: this.i18n.t("manageVoting.manage"),
                            onClick: () => this.showVotingDialog(),
                        });
                    }

                    if (room && !isOwner) {
                        this._appendActionItem(scroller, {
                            id: "sv-vote",
                            icon: "group_add",
                            title: this.i18n.t("joinVoting"),
                            subtitle: this.i18n.t("joinVoting.subtitle"),
                            onClick: () => this.joinVoting(),
                        });
                    }
                }
            } catch (_) {
                /* ignore */
            }
        });
        this._menuObserver.observe(target, { childList: true, subtree: true });
        // Also try immediately in case a sheet is already open when we attach
        this._tryInjectIntoOpenSheets();
    }

    async _tryInjectIntoOpenSheets() {
        try {
            // Try multiple selectors for different Jellyfin Web versions
            const sheets = document.querySelectorAll(".actionSheet.syncPlayGroupMenu.opened, .actionSheet.syncPlayGroupMenu, .dialogContainer .syncPlayGroupMenu");
            for (const sheet of sheets) {
                // Skip if not visible
                if (sheet.offsetParent === null && !sheet.classList.contains("opened")) continue;
                const scroller = sheet.querySelector(".actionSheetScroller, .scrollerContainer, .dialogContent");
                if (!scroller || scroller.querySelector(".svSyncVoteItem")) continue;
                console.log("[SyncVote] _tryInjectIntoOpenSheets: Found SyncPlay menu");

                const groupId = await this.getCurrentSyncPlayGroupId();
                if (!groupId) continue;
                const rooms = await this.apiClient.ajax({ type: "GET", url: this.apiClient.getUrl("SyncVote/Rooms") }).catch(() => []);
                const room = (rooms || []).find((r) => r.SyncPlayGroupId === groupId) || null;
                this.currentRoom = room;
                const userId = await this.apiClient.getCurrentUserId();
                const isOwner = !!room && room.OrganizerId && room.OrganizerId.toLowerCase() === (userId || "").toLowerCase();

                if (!room) {
                    this._appendActionItem(scroller, {
                        id: "sv-create",
                        icon: "how_to_vote",
                        title: this.i18n.t("createVoting"),
                        subtitle: this.i18n.t("createVoting.subtitle"),
                        onClick: () => this.showVotingDialog(),
                    });
                }
                if (room && isOwner) {
                    this._appendActionItem(scroller, {
                        id: "sv-manage",
                        icon: "how_to_vote",
                        title: room.IsVotingActive ? this.i18n.t("manageVoting.manage") : this.i18n.t("manageVoting.start"),
                        subtitle: room.IsVotingActive ? this.i18n.t("manageVoting.subtitle.manage") : this.i18n.t("manageVoting.subtitle.start"),
                        onClick: () => this.showVotingInterface(),
                    });
                    this._appendActionItem(scroller, {
                        id: "sv-results",
                        icon: "emoji_events",
                        title: this.i18n.t("results.show"),
                        subtitle: this.i18n.t("results.subtitle"),
                        onClick: () => this.showCurrentResults?.(),
                    });
                    this._appendActionItem(scroller, {
                        id: "sv-settings",
                        icon: "settings",
                        title: this.i18n.t("config.title"),
                        subtitle: this.i18n.t("manageVoting.manage"),
                        onClick: () => this.showVotingDialog(),
                    });
                }
                if (room && !isOwner) {
                    this._appendActionItem(scroller, {
                        id: "sv-vote",
                        icon: "group_add",
                        title: this.i18n.t("joinVoting"),
                        subtitle: this.i18n.t("joinVoting.subtitle"),
                        onClick: () => this.joinVoting(),
                    });
                }
            }
        } catch (_) {
            /* ignore */
        }
    }

    _appendActionItem(container, { id, icon, title, subtitle, onClick }) {
        const btn = document.createElement("button");
        btn.setAttribute("is", "emby-button");
        btn.type = "button";
        // Do NOT add 'actionSheetMenuItem' to avoid interfering with the sheet's internal resolution
        btn.className = "listItem listItem-button listItem-border emby-button svSyncVoteItem";
        btn.dataset.id = id;
        btn.innerHTML = `
            <span class="actionsheetMenuItemIcon listItemIcon listItemIcon-transparent material-icons ${icon}" aria-hidden="true"></span>
            <div class="listItemBody actionsheetListItemBody">
                <div class="listItemBodyText actionSheetItemText">${title}</div>
                <div class="listItemBodyText secondary">${subtitle || ""}</div>
            </div>
        `;
        btn.addEventListener("click", (e) => {
            e.preventDefault();
            onClick && onClick();
        });
        container.appendChild(btn);
    }

    _openSyncPlaySettings(sheetRoot) {
        try {
            const settingsBtn = sheetRoot.querySelector('[data-id="settings"], .actionSheetMenuItem[data-id="settings"]');
            if (settingsBtn) settingsBtn.click();
        } catch (_) {
            /* ignore */
        }
    }

    showJoinVotingButton() {
        const syncPlayContainer = document.querySelector(".syncPlayContainer");
        if (syncPlayContainer && !document.querySelector(".joinVoteButton")) {
            const button = document.createElement("button");
            button.className = "joinVoteButton raised button-submit";
            button.innerHTML = '<i class="material-icons">how_to_vote</i><span>Join Voting</span>';
            button.onclick = () => this.joinVoting();

            syncPlayContainer.appendChild(button);
        }
    }

    async updateSyncVoteControls(groupId) {
        const container = document.querySelector(".syncPlayContainer, .playerSettings, .videoOsdSettings, .actionSheetContent, .dialogContent");
        if (!container) return;
        // Skip the main group menu sheet to avoid breaking its lifecycle
        const sheet = container.closest(".actionSheet.syncPlayGroupMenu");
        if (sheet) return;

        // Clear existing controls we manage
        container.querySelectorAll(".syncVoteButton, .joinVoteButton, .svOwnerBtn, .svParticipantBtn").forEach((el) => el.remove());

        // Resolve room and permissions
        const rooms = await this.apiClient.ajax({ type: "GET", url: this.apiClient.getUrl("SyncVote/Rooms") }).catch(() => []);
        const room = (rooms || []).find((r) => r.SyncPlayGroupId === groupId) || null;
        this.currentRoom = room;
        const userId = await this.apiClient.getCurrentUserId();
        const isOwner = !!room && room.OrganizerId && room.OrganizerId.toLowerCase() === (userId || "").toLowerCase();
        const perms = await this.apiClient.ajax({ type: "GET", url: this.apiClient.getUrl("SyncVote/Permissions") }).catch(() => ({ CanOrganize: false }));

        if (!room) {
            if (!perms || !perms.CanOrganize) return; // Nothing to render for non-organizers
            const btn = document.createElement("button");
            btn.className = "syncVoteButton raised button-submit svOwnerBtn";
            btn.innerHTML = `<i class="material-icons">how_to_vote</i><span>${this.i18n.t("createVoting")}</span>`;
            btn.onclick = () => this.showVotingDialog();
            container.appendChild(btn);
            return;
        }

        if (isOwner) {
            if (!room.IsVotingActive) {
                const startBtn = document.createElement("button");
                startBtn.className = "raised button-submit svOwnerBtn";
                startBtn.innerHTML = `<i class="material-icons">play_arrow</i><span>${this.i18n.t("manageVoting.start")}</span>`;
                startBtn.onclick = async () => {
                    await this.apiClient.ajax({ type: "POST", url: this.apiClient.getUrl(`SyncVote/Room/${room.Id}/StartVoting`) });
                    this.showVotingInterface?.();
                };
                container.appendChild(startBtn);
            }
            const panelBtn = document.createElement("button");
            panelBtn.className = "raised svOwnerBtn";
            panelBtn.innerHTML = `<i class="material-icons">how_to_vote</i><span>${this.i18n.t("btn.openPanel")}</span>`;
            panelBtn.onclick = () => this.showVotingInterface();
            container.appendChild(panelBtn);
        } else {
            const joinBtn = document.createElement("button");
            joinBtn.className = "joinVoteButton raised button-submit svParticipantBtn";
            joinBtn.innerHTML = `<i class="material-icons">group_add</i><span>${this.i18n.t("joinVoting")}</span>`;
            joinBtn.onclick = () => this.joinVoting();
            container.appendChild(joinBtn);
        }
    }

    async showVotingDialog() {
        // Load library data first
        await this._loadLibraryData();

        // Create voting room creation dialog
        const dialogHtml = this.getVotingDialogHtml();

        const dialogOptions = {
            title: this.i18n.t("dialog.createRoom.title"),
            text: dialogHtml,
            html: true,
            buttons: [
                {
                    name: this.i18n.t("btn.close"),
                    id: "cancel",
                    type: "cancel",
                },
                {
                    name: this.i18n.t("btn.createRoom"),
                    id: "create",
                    type: "submit",
                },
            ],
        };

        // Wire up events after a small delay to allow DOM to render
        setTimeout(() => this._wireDialogEvents(), 100);

        const dialog = await (typeof Dashboard !== "undefined" && Dashboard.alert ? Dashboard.alert(dialogOptions) : Promise.resolve(null));

        if (dialog === "create") {
            await this.createVotingRoom();
        }
    }

    async _loadLibraryData() {
        try {
            // Load collections, genres, and ratings in parallel
            const [collections, genres, ratings, syncPlayInfo] = await Promise.all([this.apiClient.ajax({ type: "GET", url: this.apiClient.getUrl("SyncVote/Library/Collections") }).catch(() => []), this.apiClient.ajax({ type: "GET", url: this.apiClient.getUrl("SyncVote/Library/Genres") }).catch(() => []), this.apiClient.ajax({ type: "GET", url: this.apiClient.getUrl("SyncVote/Library/ParentalRatings") }).catch(() => []), this.apiClient.ajax({ type: "GET", url: this.apiClient.getUrl("SyncVote/SyncPlayInfo") }).catch(() => ({}))]);

            this._libraryCollections = collections || [];
            this._libraryGenres = genres || [];
            this._parentalRatings = ratings || [];
            this._syncPlayInfo = syncPlayInfo || {};
        } catch (e) {
            console.error("Error loading library data:", e);
            this._libraryCollections = [];
            this._libraryGenres = [];
            this._parentalRatings = [];
            this._syncPlayInfo = {};
        }
    }

    async _checkAccessForCollections(collectionIds) {
        if (!collectionIds || collectionIds.length === 0) return { HasAccessIssues: false };
        try {
            return await this.apiClient.ajax({
                type: "POST",
                url: this.apiClient.getUrl("SyncVote/CheckAccess"),
                data: JSON.stringify({ CollectionIds: collectionIds }),
                contentType: "application/json",
            });
        } catch (e) {
            console.error("Error checking access:", e);
            return { HasAccessIssues: false };
        }
    }

    getVotingDialogHtml() {
        // Build collections checkboxes
        const collectionsHtml = this._libraryCollections
            .map(
                (c) => `
            <label class="sv-checkbox-item">
                <input type="checkbox" class="sv-collection-check" value="${c.Id}" data-name="${c.Name}">
                <span>${c.Name}</span>
                <span class="sv-item-count">(${c.ItemCount})</span>
            </label>
        `,
            )
            .join("");

        // Build genres checkboxes (limit display, expandable)
        const topGenres = this._libraryGenres.slice(0, 12);
        const moreGenres = this._libraryGenres.slice(12);
        const genresHtml = topGenres
            .map(
                (g) => `
            <label class="sv-checkbox-item sv-genre-item">
                <input type="checkbox" class="sv-genre-check" value="${g}">
                <span>${g}</span>
            </label>
        `,
            )
            .join("");
        const moreGenresHtml = moreGenres
            .map(
                (g) => `
            <label class="sv-checkbox-item sv-genre-item">
                <input type="checkbox" class="sv-genre-check" value="${g}">
                <span>${g}</span>
            </label>
        `,
            )
            .join("");

        // Build parental ratings select
        const ratingsHtml = this._parentalRatings
            .map(
                (r) => `
            <option value="${r.Value}">${r.Name}</option>
        `,
            )
            .join("");

        const memberCount = this._syncPlayInfo?.MemberCount || 1;

        return `
            <div class="sv-dialog-content">
                <div class="sv-access-warning" id="svAccessWarning" style="display:none;">
                    <span class="material-icons">warning</span>
                    <span>${this.i18n.t("warning.accessIssues")}</span>
                </div>

                <div class="inputContainer">
                    <label for="roomName">${this.i18n.t("field.roomName")}</label>
                    <input type="text" id="roomName" class="emby-input" placeholder="${this.i18n.t("field.roomName")}" required>
                </div>

                <div class="sv-section">
                    <h3 class="sv-section-title">
                        <span class="material-icons">folder</span>
                        ${this.i18n.t("field.collections")}
                    </h3>
                    <p class="sv-section-desc">${this.i18n.t("field.collections.desc")}</p>
                    <div class="sv-checkbox-grid" id="svCollections">
                        ${collectionsHtml || `<p class="sv-empty">${this.i18n.t("empty.collections")}</p>`}
                    </div>
                </div>

                <div class="sv-section">
                    <h3 class="sv-section-title">
                        <span class="material-icons">category</span>
                        ${this.i18n.t("field.genres")}
                    </h3>
                    <div class="sv-checkbox-grid sv-genres-grid" id="svGenres">
                        ${genresHtml}
                        ${moreGenresHtml ? `<div class="sv-more-genres" id="svMoreGenres" style="display:none;">${moreGenresHtml}</div>` : ""}
                    </div>
                    ${
                        moreGenres.length > 0
                            ? `
                        <button type="button" class="sv-show-more-btn" id="svShowMoreGenres">
                            ${this.i18n.t("btn.showMore")} (${moreGenres.length})
                        </button>
                    `
                            : ""
                    }
                </div>

                <div class="sv-section sv-filters-row">
                    <div class="inputContainer sv-filter-item">
                        <label for="maxRating">${this.i18n.t("field.maxRating")}</label>
                        <select id="maxRating" class="emby-select">
                            <option value="">${this.i18n.t("filter.noLimit")}</option>
                            ${ratingsHtml}
                        </select>
                    </div>

                    <div class="inputContainer sv-filter-item">
                        <label for="timeLimit">${this.i18n.t("field.timeLimit")}</label>
                        <select id="timeLimit" class="emby-select">
                            <option value="1">1 ${this.i18n.t("time.minutes")}</option>
                            <option value="3">3 ${this.i18n.t("time.minutes")}</option>
                            <option value="5" selected>5 ${this.i18n.t("time.minutes")}</option>
                            <option value="10">10 ${this.i18n.t("time.minutes")}</option>
                            <option value="15">15 ${this.i18n.t("time.minutes")}</option>
                        </select>
                    </div>

                    <div class="inputContainer sv-filter-item">
                        <label for="sortBy">${this.i18n.t("field.sortBy")}</label>
                        <select id="sortBy" class="emby-select">
                            <option value="Random" selected>${this.i18n.t("sort.random")}</option>
                            <option value="Title">${this.i18n.t("sort.title")}</option>
                            <option value="CommunityRating">${this.i18n.t("sort.rating")}</option>
                            <option value="PremiereDate">${this.i18n.t("sort.release")}</option>
                        </select>
                    </div>
                </div>

                <div class="sv-info-bar">
                    <span class="material-icons">group</span>
                    <span>${this.i18n.t("info.syncPlayMembers", memberCount)}</span>
                </div>
            </div>
        `;
    }

    _wireDialogEvents() {
        // Wire up collection change to check access
        const collectionChecks = document.querySelectorAll(".sv-collection-check");
        const accessWarning = document.getElementById("svAccessWarning");
        let checkTimeout = null;
        const self = this;

        const debounceCheckAccess = () => {
            if (checkTimeout) clearTimeout(checkTimeout);
            checkTimeout = setTimeout(async () => {
                const selected = Array.from(collectionChecks)
                    .filter((c) => c.checked)
                    .map((c) => c.value);
                if (selected.length > 0) {
                    const result = await self._checkAccessForCollections(selected);
                    if (result && result.HasAccessIssues) {
                        accessWarning.style.display = "flex";
                    } else {
                        accessWarning.style.display = "none";
                    }
                } else {
                    accessWarning.style.display = "none";
                }
            }, 500);
        };

        collectionChecks.forEach((cb) => {
            cb.addEventListener("change", debounceCheckAccess);
        });

        // Show more genres button
        const showMoreBtn = document.getElementById("svShowMoreGenres");
        const moreGenresDiv = document.getElementById("svMoreGenres");
        if (showMoreBtn && moreGenresDiv) {
            const showMoreText = this.i18n.t("btn.showMore");
            const showLessText = this.i18n.t("btn.showLess");
            const moreCount = this._libraryGenres.length > 12 ? this._libraryGenres.length - 12 : 0;

            showMoreBtn.addEventListener("click", () => {
                const isHidden = moreGenresDiv.style.display === "none";
                moreGenresDiv.style.display = isHidden ? "contents" : "none";
                showMoreBtn.textContent = isHidden ? showLessText : `${showMoreText} (${moreCount})`;
            });
        }
    }

    async createVotingRoom() {
        const roomName = document.getElementById("roomName")?.value;
        const timeLimit = parseInt(document.getElementById("timeLimit")?.value || "5");
        const sortBy = document.getElementById("sortBy")?.value || "Random";
        const maxRating = document.getElementById("maxRating")?.value;

        // Get selected collections
        const selectedCollections = Array.from(document.querySelectorAll(".sv-collection-check:checked")).map((cb) => cb.value);

        // Get selected genres
        const selectedGenres = Array.from(document.querySelectorAll(".sv-genre-check:checked")).map((cb) => cb.value);

        if (!roomName) {
            svAlert(this.i18n.t("msg.enterRoomName"));
            return;
        }

        try {
            // Get current SyncPlay group ID from active session
            const syncPlayGroupId = await this.getCurrentSyncPlayGroupId();

            const requestData = {
                Name: roomName,
                SyncPlayGroupId: syncPlayGroupId,
                TimeLimit: timeLimit,
                SortBy: sortBy,
                SelectedCollections: selectedCollections,
                SelectedGenres: selectedGenres,
                ItemTypes: ["Movie"],
            };

            // Add parental rating if selected
            if (maxRating && maxRating !== "") {
                requestData.MaxParentalRating = parseInt(maxRating);
            }

            const room = await this.apiClient.ajax({
                type: "POST",
                url: this.apiClient.getUrl("SyncVote/Room"),
                data: JSON.stringify(requestData),
                contentType: "application/json",
            });

            this.currentRoom = room;
            svAlert(this.i18n.t("msg.roomCreated"));
            this.showVotingInterface();
        } catch (error) {
            console.error("Error creating voting room:", error);
            svAlert(this.i18n.t("msg.errorCreatingRoom"));
        }
    }

    async joinVoting() {
        if (!this.currentRoom) return;

        try {
            await this.apiClient.ajax({
                type: "POST",
                url: this.apiClient.getUrl(`SyncVote/Room/${this.currentRoom.Id}/Join`),
            });

            this.showVotingInterface();
        } catch (error) {
            console.error("Error joining voting room:", error);
            svAlert(this.i18n.t("msg.errorJoining"));
        }
    }

    showVotingInterface() {
        // Create and show the voting interface
        const votingHtml = this.getVotingInterfaceHtml();

        const dialogOptions = {
            title: `${this.i18n.t("title")}: ${this.currentRoom.Name}`,
            text: votingHtml,
            html: true,
            size: "large",
            buttons: [],
        };
        if (typeof Dashboard !== "undefined" && Dashboard.alert) {
            Dashboard.alert(dialogOptions);
        }
        this.initializeVotingInterface();
    }

    async showCurrentResults() {
        try {
            if (!this.currentRoom) return;
            const results = await this.apiClient.ajax({
                type: "GET",
                url: this.apiClient.getUrl(`SyncVote/Room/${this.currentRoom.Id}/Results`),
            });
            this.showResults(results);
        } catch (e) {
            console.error("Error fetching current results", e);
        }
    }

    getVotingInterfaceHtml() {
        return `
            <div id="votingInterface" class="votingInterface">
                <div class="votingHeader">
                    <div class="timeRemaining">
                        <span id="timeDisplay">5:00</span>
                    </div>
                    <div class="progress">
                        <div id="progressBar" class="progressBar"></div>
                    </div>
                </div>

                <div class="movieCard" id="currentMovie">
                    <div class="moviePoster">
                        <img id="movieImage" src="" alt="Movie Poster">
                    </div>
                    <div class="movieInfo">
                        <h3 id="movieTitle"></h3>
                        <p id="movieDetails"></p>
                    </div>
                </div>

                <div class="votingButtons">
                    <button id="dislikeBtn" class="voteButton dislike">
                        <i class="material-icons">thumb_down</i>
                    </button>
                    <button id="likeBtn" class="voteButton like">
                        <i class="material-icons">thumb_up</i>
                    </button>
                </div>

                <div class="votingStats">
                    <span id="votesCount">0 votes cast</span>
                </div>
            </div>
        `;
    }

    initializeVotingInterface() {
        // Initialize voting interface logic
        document.getElementById("likeBtn").onclick = () => this.vote(true);
        document.getElementById("dislikeBtn").onclick = () => this.vote(false);

        // Start voting timer
        this.startVotingTimer();

        // Load first movie
        this.loadNextMovie();
    }

    async vote(isLike) {
        if (!this.currentMovie) return;

        try {
            await this.apiClient.ajax({
                type: "POST",
                url: this.apiClient.getUrl("SyncVote/Vote"),
                data: JSON.stringify({
                    RoomId: this.currentRoom.Id,
                    ItemId: this.currentMovie.Id,
                    IsLike: isLike,
                }),
                contentType: "application/json",
            });

            this.votesCount++;
            document.getElementById("votesCount").textContent = `${this.votesCount} votes cast`;

            // Load next movie
            this.loadNextMovie();
        } catch (error) {
            console.error("Error casting vote:", error);
        }
    }

    async loadNextMovie() {
        try {
            if (!this._candidates || this._candidateIndex >= this._candidates.length) {
                const sortBy = this.currentRoom?.SortBy || "Random";
                const query = {
                    IncludeItemTypes: "Movie",
                    Recursive: true,
                    Limit: 50,
                    Fields: "Genres,ProductionYear,PrimaryImageAspectRatio",
                };
                if (sortBy === "Title") {
                    query.SortBy = "SortName";
                    query.SortOrder = "Ascending";
                } else if (sortBy === "CommunityRating") {
                    query.SortBy = "CommunityRating";
                    query.SortOrder = "Descending";
                } else if (sortBy === "PremiereDate") {
                    query.SortBy = "PremiereDate";
                    query.SortOrder = "Descending";
                } else {
                    query.SortBy = "Random";
                }
                const url = this.apiClient.getUrl("Items", query);
                const res = await this.apiClient.ajax({ type: "GET", url });
                this._candidates = res?.Items || [];
                this._candidateIndex = 0;
            }

            this.currentMovie = this._candidates[this._candidateIndex++] || null;
            if (this.currentMovie) {
                document.getElementById("movieTitle").textContent = this.currentMovie.Name || "";
                const year = this.currentMovie.ProductionYear ? `${this.currentMovie.ProductionYear}` : "";
                const genres = this.currentMovie.Genres && this.currentMovie.Genres.length ? this.currentMovie.Genres.join(", ") : "";
                document.getElementById("movieDetails").textContent = [year, genres].filter(Boolean).join(" • ");
                document.getElementById("movieImage").src = this.apiClient.getImageUrl(this.currentMovie.Id, { type: "Primary", maxWidth: 300 });
            }
        } catch (e) {
            console.error("Error loading next item", e);
        }
    }

    startVotingTimer() {
        let timeRemaining = this.currentRoom.TimeLimit * 60; // Convert to seconds

        this.votingTimer = setInterval(() => {
            timeRemaining--;

            const minutes = Math.floor(timeRemaining / 60);
            const seconds = timeRemaining % 60;
            document.getElementById("timeDisplay").textContent = `${minutes}:${seconds.toString().padStart(2, "0")}`;

            if (timeRemaining <= 0) {
                this.endVoting();
            }
        }, 1000);
    }

    async endVoting() {
        if (this.votingTimer) {
            clearInterval(this.votingTimer);
            this.votingTimer = null;
        }

        try {
            const results = await this.apiClient.ajax({
                type: "GET",
                url: this.apiClient.getUrl(`SyncVote/Room/${this.currentRoom.Id}/Results`),
            });

            this.showResults(results);
        } catch (error) {
            console.error("Error getting voting results:", error);
        }
    }

    showResults(results) {
        const resultsHtml = this.getResultsHtml(results);

        const dialogOptions = {
            title: this.i18n.t("results.title"),
            text: resultsHtml,
            html: true,
            size: "large",
            buttons: [
                {
                    name: this.i18n.t("btn.playWinner"),
                    id: "play",
                    type: "submit",
                },
                {
                    name: this.i18n.t("btn.close"),
                    id: "close",
                    type: "cancel",
                },
            ],
        };
        (typeof Dashboard !== "undefined" && Dashboard.alert ? Dashboard.alert(dialogOptions) : Promise.resolve()).then((result) => {
            if (result === "play" && results.Winner) {
                this.playWinner(results.Winner);
            }
        });
    }

    getResultsHtml(results) {
        if (!results.Winner) {
            return `<p>${this.i18n.t("results.none")}</p>`;
        }

        let html = `
            <div class="votingResults">
                <div class="winner">
                    <h3>🏆 ${this.i18n.t("results.winner")}: ${results.Winner.Name}</h3>
                    <p>${results.Winner.VoteCount} ${this.i18n.t("votes.word")}</p>
                </div>
        `;

        if (results.LikedItems.length > 1) {
            html += '<div class="otherResults"><h4>Other liked movies:</h4><ul>';
            results.LikedItems.slice(1).forEach((item) => {
                html += `<li>${item.Name} (${item.VoteCount} ${this.i18n.t("votes.word")})</li>`;
            });
            html += "</ul></div>";
        }

        html += "</div>";
        return html;
    }

    async playWinner(winner) {
        // Integrate with SyncPlay to start playing the winning movie
        try {
            // This would use SyncPlay API to queue and play the winner
            await this.apiClient.ajax({
                type: "POST",
                url: this.apiClient.getUrl("SyncPlay/Queue"),
                data: JSON.stringify({
                    ItemIds: [winner.ItemId],
                    PlayCommand: "PlayNow",
                }),
                contentType: "application/json",
            });

            svAlert(this.i18n.t("play.starting"));
        } catch (error) {
            console.error("Error starting playback:", error);
            svAlert(this.i18n.t("play.error"));
        }
    }

    async getCurrentSyncPlayGroupId() {
        try {
            const userId = await this.apiClient.getCurrentUserId();
            const url = this.apiClient.getUrl("Sessions");
            const sessions = await this.apiClient.ajax({ type: "GET", url });
            const forUser = (sessions || []).filter((s) => s?.UserId?.toLowerCase?.() === (userId || "").toLowerCase());
            const withGroup = forUser.find((s) => !!s?.SyncPlayGroupId);
            return withGroup?.SyncPlayGroupId || (forUser[0]?.SyncPlayGroupId ?? null);
        } catch (e) {
            return null;
        }
    }
}

// Initialize SyncVote manager even if loaded after DOMContentLoaded
function initSyncVote() {
    if (window.__syncVoteManager) return;

    // Wait for ApiClient to be available
    if (!window.ApiClient) {
        console.log("[SyncVote] ApiClient not yet available, retrying in 500ms...");
        setTimeout(initSyncVote, 500);
        return;
    }

    try {
        window.__syncVoteManager = new SyncVoteManager();
        console.log("[SyncVote] SyncVoteManager initialized successfully");
    } catch (e) {
        console.error("[SyncVote] Error initializing SyncVoteManager:", e);
    }
    return;
}

// Try to initialize immediately if DOM is ready
if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", function () {
        console.log("[SyncVote] DOMContentLoaded fired");
        initSyncVote();
    });
} else {
    console.log("[SyncVote] DOM already loaded, initializing now");
    initSyncVote();
}

// Also inject when dashboard is loaded (for single page app navigation)
try {
    if (typeof Events !== "undefined" && typeof Emby !== "undefined" && Emby.Page) {
        Events.on(Emby.Page, "pageshow", function () {
            initSyncVote();
        });
    }
} catch (e) {
    console.log("[SyncVote] Emby.Page events not available, using fallback");
}

// Fallback: periodically check if we need to reinitialize
setInterval(function () {
    if (!window.__syncVoteManager && window.ApiClient) {
        initSyncVote();
    }
}, 2000);
