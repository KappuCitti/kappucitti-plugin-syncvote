// SyncVote - client script for the Jellyfin web interface.
// The server adds it to index.html (see WebClientInjectionMiddleware); it is served from /SyncVote/Client/syncvote.js.
// The only entry point is the "SyncVote" item in the SyncPlay menu. Dialogs reuse Jellyfin's own markup and components.
(function () {
    "use strict";

    if (window.__syncVoteLoaded) {
        return;
    }
    window.__syncVoteLoaded = true;

    var LOG = "[SyncVote]";
    var ROOM_POLL_MS = 2000;

    // ---------------------------------------------------------------- helpers

    function esc(value) {
        return String(value === null || value === undefined ? "" : value).replace(/[&<>"']/g, function (c) {
            return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c];
        });
    }

    function same(a, b) {
        return !!a && !!b && String(a).replace(/-/g, "").toLowerCase() === String(b).replace(/-/g, "").toLowerCase();
    }

    function client() {
        return window.ApiClient || null;
    }

    function authHeaders() {
        var headers = {};
        var api = client();
        if (!api) {
            return headers;
        }
        try {
            if (typeof api.setRequestHeaders === "function") {
                api.setRequestHeaders(headers);
            }
        } catch (e) {
            /* fall back below */
        }
        if (!headers.Authorization && !headers["X-Emby-Authorization"] && typeof api.accessToken === "function" && api.accessToken()) {
            headers.Authorization = 'MediaBrowser Token="' + api.accessToken() + '"';
        }
        return headers;
    }

    // Calls the SyncVote API. Resolves with the parsed JSON (null when there is no body); rejects with an Error that has .status.
    function call(method, path, body) {
        var headers = authHeaders();
        var options = { method: method, headers: headers };
        if (body !== undefined) {
            headers["Content-Type"] = "application/json";
            options.body = JSON.stringify(body);
        }
        return fetch(client().getUrl(path), options).then(function (response) {
            if (!response.ok) {
                var error = new Error(method + " " + path + " -> " + response.status);
                error.status = response.status;
                throw error;
            }
            return response.text().then(function (text) {
                return text ? JSON.parse(text) : null;
            });
        });
    }

    function sleep(ms) {
        return new Promise(function (resolve) {
            setTimeout(resolve, ms);
        });
    }

    // ------------------------------------------------------------------- i18n

    // The server picks the language (its own display language, or the one set in the plugin options) and sends the strings.
    // They are asked for again every time the panel opens, so a change of language needs no page reload.
    var i18n = {
        dict: {},
        language: "en",
        load: function () {
            var self = this;
            return fetch(client().getUrl("SyncVote/Client/i18n"), { cache: "no-store" })
                .then(function (r) {
                    return r.ok ? r.json() : null;
                })
                .catch(function () {
                    return null;
                })
                .then(function (result) {
                    if (result && result.Strings) {
                        self.dict = result.Strings;
                        self.language = result.Language;
                    }
                });
        },
    };

    function t(key) {
        var text = i18n.dict[key] || key;
        for (var i = 1; i < arguments.length; i++) {
            text = text.replace("{" + (i - 1) + "}", arguments[i]);
        }
        return text;
    }

    function toast(message) {
        var el = document.createElement("div");
        el.className = "svToast";
        el.textContent = message;
        document.body.appendChild(el);
        setTimeout(function () {
            el.remove();
        }, 3500);
    }

    // ----------------------------------------------- Jellyfin-style dialog

    // Same markup as the dialogs of Jellyfin web (formDialog), so theme, fonts and components match.
    var dialog = {
        container: null,
        backdrop: null,
        onClose: null,

        // Replacing the dialog with another view is not a "close": the old handler must not run.
        open: function (title, onClose) {
            this.close(true);
            var backdrop = document.createElement("div");
            backdrop.className = "dialogBackdrop dialogBackdropOpened";
            var container = document.createElement("div");
            container.className = "dialogContainer svDialogContainer";
            container.innerHTML =
                '<div class="focuscontainer dialog smoothScrollY dialog-fixedSize dialog-small formDialog opened svDialog" data-removeonclose="true" style="animation: 180ms ease-out 0s 1 normal both running scaleup;">' +
                '<div class="formDialogHeader">' +
                '<button is="paper-icon-button-light" type="button" class="btnCancel autoSize" tabindex="-1" title="' + esc(t("btn.close")) + '"><span class="material-icons arrow_back" aria-hidden="true"></span></button>' +
                '<h3 class="formDialogHeaderTitle"></h3>' +
                "</div>" +
                '<div class="formDialogContent smoothScrollY"><div class="dialogContentInner dialog-content-centered svBody"></div></div>' +
                '<div class="formDialogFooter svFooter"></div>' +
                "</div>";
            container.querySelector(".formDialogHeaderTitle").textContent = title || t("title");
            container.querySelector(".btnCancel").addEventListener("click", function () {
                dialog.close();
            });
            backdrop.addEventListener("click", function () {
                dialog.close();
            });
            document.body.appendChild(backdrop);
            document.body.appendChild(container);
            this.backdrop = backdrop;
            this.container = container;
            this.onClose = onClose || null;
            return this.body();
        },

        isOpen: function () {
            return !!this.container;
        },

        body: function () {
            return this.container ? this.container.querySelector(".svBody") : null;
        },

        footer: function () {
            return this.container ? this.container.querySelector(".svFooter") : null;
        },

        close: function (silent) {
            if (!this.container) {
                return;
            }
            var handler = silent === true ? null : this.onClose;
            this.container.remove();
            this.backdrop.remove();
            this.container = null;
            this.backdrop = null;
            this.onClose = null;
            if (handler) {
                handler();
            }
        },
    };

    document.addEventListener("keydown", function (e) {
        if (e.key === "Escape" && dialog.isOpen()) {
            dialog.close();
        }
    });
    window.addEventListener("hashchange", function () {
        dialog.close();
    });

    // Native components. They are upgraded by Jellyfin as soon as they are in the DOM.
    function btn(label, cls, icon) {
        return '<button is="emby-button" type="button" class="raised block formDialogFooterItem ' + cls + '">' + (icon ? '<span class="material-icons ' + icon + '" aria-hidden="true"></span>' : "") + "<span>" + esc(label) + "</span></button>";
    }

    function checkbox(cls, value, label, extra, checked) {
        return '<label class="emby-checkbox-label"><input type="checkbox" is="emby-checkbox" class="' + cls + '" value="' + esc(value) + '"' + (checked ? " checked" : "") + '><span class="checkboxLabel">' + esc(label) + (extra ? ' <span class="secondaryText">(' + esc(extra) + ")</span>" : "") + "</span></label>";
    }

    function setFooter(html) {
        var footer = dialog.footer();
        if (footer) {
            footer.innerHTML = html;
            footer.style.display = html ? "" : "none";
        }
    }

    // "1 vote", "3 votes"
    function votes(count) {
        return count + " " + t(count === 1 ? "votes.one" : "votes.word");
    }

    function listItem(icon, primary, secondary) {
        return '<div class="listItem listItem-border"><span class="listItemIcon material-icons ' + icon + '" aria-hidden="true"></span><div class="listItemBody"><div class="listItemBodyText">' + esc(primary) + "</div>" + (secondary ? '<div class="listItemBodyText secondary">' + esc(secondary) + "</div>" : "") + "</div></div>";
    }

    // ---------------------------------------------------------------- manager

    function SyncVoteManager() {
        this.group = null;
        this.room = null;
        this.userId = null;
        this.queue = [];
        this.seen = {};
        this.current = null;
        this.votes = 0;
        this.exhausted = false;
        this.loading = false;
        this.voting = false;
        this.pollTimer = null;
        this.tickTimer = null;
        this.remaining = 0;
        this.total = 1;
        this.view = null;
    }

    SyncVoteManager.prototype.start = function () {
        var self = this;
        i18n.load();
        // The SyncPlay menu is created on demand: watch for it and add our item.
        new MutationObserver(function () {
            self.injectMenuItem();
        }).observe(document.body, { childList: true, subtree: true });
        console.log(LOG, "ready");
    };

    SyncVoteManager.prototype.currentUserId = function () {
        var api = client();
        return Promise.resolve(api && api.getCurrentUserId ? api.getCurrentUserId() : null);
    };

    // ------------------------------------------------------- SyncPlay menu

    // The menu of a SyncPlay group has "leave-group" in it. Outside a group the menu offers to create/join one: no SyncVote there.
    SyncVoteManager.prototype.injectMenuItem = function () {
        var self = this;

        document.querySelectorAll(".actionSheet.syncPlayGroupMenu").forEach(function (sheet) {
            var scroller = sheet.querySelector(".actionSheetScroller");
            if (!scroller || scroller.querySelector(".svSyncVoteItem") || !scroller.querySelector('[data-id="leave-group"]')) {
                return;
            }
            var item = document.createElement("button");
            item.type = "button";
            item.setAttribute("is", "emby-button");
            item.className = "listItem listItem-button actionSheetMenuItem listItem-border emby-button svSyncVoteItem";
            item.innerHTML = '<span class="actionsheetMenuItemIcon listItemIcon listItemIcon-transparent material-icons how_to_vote" aria-hidden="true"></span>' + '<div class="listItemBody actionsheetListItemBody"><div class="listItemBodyText actionSheetItemText">' + esc(t("menu.open")) + '</div><div class="listItemBodyText secondary">' + esc(t("menu.open.subtitle")) + "</div></div>";
            item.addEventListener("click", function (e) {
                e.preventDefault();
                e.stopPropagation();
                self.closeMenu(sheet).then(function () {
                    return self.openPanel();
                });
            });
            scroller.appendChild(item);
        });

        // Newer web clients (Material UI menu).
        document.querySelectorAll('#app-sync-play-menu, [role="menu"]').forEach(function (menu) {
            if (menu.querySelector(".svSyncVoteItem") || !/leave group|lascia il gruppo/i.test(menu.textContent || "")) {
                return;
            }
            var li = document.createElement("li");
            li.className = "MuiButtonBase-root MuiMenuItem-root MuiMenuItem-gutters svSyncVoteItem";
            li.setAttribute("role", "menuitem");
            li.setAttribute("tabindex", "-1");
            li.style.cssText = "display:flex;align-items:center;gap:12px;padding:6px 16px;cursor:pointer;";
            li.innerHTML = '<span class="material-icons" aria-hidden="true">how_to_vote</span><span>' + esc(t("menu.open")) + "</span>";
            li.addEventListener("click", function (e) {
                e.preventDefault();
                e.stopPropagation();
                var backdrop = (menu.closest(".MuiPopover-root, .MuiMenu-root") || document).querySelector(".MuiBackdrop-root");
                if (backdrop) {
                    backdrop.click();
                }
                sleep(250).then(function () {
                    return self.openPanel();
                });
            });
            menu.appendChild(li);
        });
    };

    // Jellyfin's action sheets are tied to the browser history: going back closes them, exactly like picking one of their own items.
    SyncVoteManager.prototype.closeMenu = function (sheet) {
        if (!document.body.contains(sheet) || !sheet.classList.contains("opened")) {
            return Promise.resolve();
        }
        history.back();
        var waited = 0;
        return new Promise(function (resolve) {
            var timer = setInterval(function () {
                waited += 50;
                if (!document.body.contains(sheet) || !sheet.classList.contains("opened") || waited >= 1500) {
                    clearInterval(timer);
                    resolve();
                }
            }, 50);
        }).then(function () {
            return sleep(120);
        });
    };

    // ------------------------------------------------------------------ panel

    SyncVoteManager.prototype.stopTimers = function () {
        clearInterval(this.pollTimer);
        clearInterval(this.tickTimer);
        this.pollTimer = null;
        this.tickTimer = null;
    };

    SyncVoteManager.prototype.openPanel = function () {
        var self = this;
        this.stopTimers();
        return i18n
            .load()
            .then(function () {
                return Promise.all([self.currentUserId(), call("GET", "SyncVote/SyncPlayInfo")]);
            })
            .then(function (result) {
                self.userId = result[0];
                self.group = result[1] && result[1].GroupId ? result[1] : null;
                if (!self.group) {
                    toast(t("msg.notInSyncPlay"));
                    return null;
                }
                return Promise.all([call("GET", "SyncVote/Rooms"), call("GET", "SyncVote/Permissions")]);
            })
            .then(function (result) {
                if (!result) {
                    return;
                }
                var room = (result[0] || []).filter(function (r) {
                    return r.SyncPlayGroupId === self.group.GroupId;
                })[0];
                if (!room) {
                    if (!(result[1] && result[1].CanOrganize)) {
                        toast(t("msg.noRoomYet"));
                        return;
                    }
                    return self.showCreate();
                }
                return self.enterRoom(room);
            })
            .catch(function (e) {
                console.error(LOG, e);
                toast(t("msg.error"));
            });
    };

    SyncVoteManager.prototype.isOrganizer = function () {
        return !!this.room && same(this.room.OrganizerId, this.userId);
    };

    SyncVoteManager.prototype.stateOf = function (room) {
        if (!room) {
            return "none";
        }
        return room.IsVotingActive ? "voting" : room.IsVotingFinished ? "results" : "lobby";
    };

    SyncVoteManager.prototype.enterRoom = function (room) {
        var self = this;
        var member = (room.MemberIds || []).some(function (id) {
            return same(id, self.userId);
        });
        var joined = member ? Promise.resolve(room) : call("POST", "SyncVote/Room/" + room.Id + "/Join");
        return joined.then(function (updated) {
            self.room = updated || room;
            self.queue = [];
            self.seen = {};
            self.current = null;
            self.votes = 0;
            self.exhausted = false;
            self.render();
            self.startPolling();
        });
    };

    SyncVoteManager.prototype.startPolling = function () {
        var self = this;
        clearInterval(this.pollTimer);
        this.pollTimer = setInterval(function () {
            self.refreshRoom();
        }, ROOM_POLL_MS);
    };

    SyncVoteManager.prototype.refreshRoom = function () {
        var self = this;
        if (!this.room || !dialog.isOpen()) {
            this.stopTimers();
            return Promise.resolve();
        }
        return call("GET", "SyncVote/Room/" + this.room.Id)
            .then(function (room) {
                var before = self.stateOf(self.room);
                self.room = room;
                if (self.stateOf(room) !== before) {
                    self.render();
                } else if (self.view === "lobby") {
                    self.renderLobbyMembers();
                } else if (self.view === "voting" && room.SecondsRemaining !== null && room.SecondsRemaining !== undefined) {
                    self.remaining = room.SecondsRemaining;
                }
            })
            .catch(function (e) {
                if (e.status === 404) {
                    self.stopTimers();
                    dialog.close();
                    toast(t("msg.roomClosed"));
                }
            });
    };

    SyncVoteManager.prototype.render = function () {
        var state = this.stateOf(this.room);
        if (state === "voting") {
            this.showVoting();
        } else if (state === "results") {
            this.showResults();
        } else {
            this.showLobby();
        }
    };

    SyncVoteManager.prototype.openDialog = function (view, title) {
        var self = this;
        this.view = view;
        this.stopTimers();
        return dialog.open(title, function () {
            self.view = null;
            self.stopTimers();
        });
    };

    // ------------------------------------------------------------ create room

    SyncVoteManager.prototype.showCreate = function () {
        var self = this;
        var body = this.openDialog("create", t("dialog.createRoom.title"));
        body.innerHTML = '<p class="fieldDescription">' + esc(t("msg.loading")) + "</p>";
        setFooter("");

        var safe = function (p) {
            return p.catch(function () {
                return [];
            });
        };
        return Promise.all([safe(call("GET", "SyncVote/Library/Collections")), safe(call("GET", "SyncVote/Library/Genres")), safe(call("GET", "SyncVote/Library/ParentalRatings"))]).then(function (data) {
            if (self.view !== "create") {
                return;
            }
            var everything = data[0] || [];
            // Library folders and Jellyfin collections (box sets) are different things: two lists.
            var folders = everything.filter(function (c) { return c.Type !== "boxset"; });
            var boxSets = everything.filter(function (c) { return c.Type === "boxset"; });
            var genres = data[1] || [];
            var ratings = data[2] || [];
            var members = (self.group && self.group.MemberCount) || 1;

            var options = function (list) {
                return list
                    .map(function (o) {
                        return '<option value="' + esc(o[0]) + '"' + (o[2] ? " selected" : "") + ">" + esc(o[1]) + "</option>";
                    })
                    .join("");
            };

            body.classList.add("svCreateBody");
            body.innerHTML =
                '<form class="svForm" onsubmit="return false">' +
                '<div id="svFormError" class="svError" role="alert" style="display:none"></div>' +
                '<div class="svRow2"><div class="inputContainer"><input is="emby-input" type="text" id="svRoomName" maxlength="60" autocomplete="off" label="' + esc(t("field.roomName")) + '"></div>' +
                '<div class="inputContainer"><input is="emby-input" type="text" id="svTitleFilter" maxlength="60" autocomplete="off" label="' + esc(t("field.titleFilter")) + '"></div></div>' +
                '<div id="svAccessWarning" class="fieldDescription" style="display:none;color:#ffc107"><span class="material-icons" style="vertical-align:middle">warning</span> ' + esc(t("warning.accessIssues")) + "</div>" +
                '<div class="svColumns"><div>' +
                '<h2 class="sectionTitle">' + esc(t("field.folders")) + "</h2>" +
                '<div class="fieldDescription">' + esc(t("field.folders.desc")) + "</div>" +
                '<div class="checkboxList svScroll">' + (folders.map(function (c) { return checkbox("svCollection", c.Id, c.Name, c.ItemCount, folders.length === 1 && boxSets.length === 0); }).join("") || '<div class="fieldDescription">' + esc(t("empty.folders")) + "</div>") + "</div>" +
                "</div>" +
                // Collections (box sets) are only offered when the server has some.
                (boxSets.length
                    ? '<div><h2 class="sectionTitle">' + esc(t("field.collections")) + "</h2>" +
                      '<div class="fieldDescription">' + esc(t("field.collections.desc")) + "</div>" +
                      '<div class="checkboxList svScroll">' + boxSets.map(function (c) { return checkbox("svCollection", c.Id, c.Name, c.ItemCount); }).join("") + "</div></div>"
                    : "") +
                "<div>" +
                '<h2 class="sectionTitle">' + esc(t("field.genres")) + "</h2>" +
                '<div class="checkboxList svScroll">' + genres.map(function (g) { return checkbox("svGenre", g, g); }).join("") + "</div>" +
                "</div></div>" +
                '<div class="svSelects">' +
                '<div class="selectContainer"><select is="emby-select" id="svMaxRating" label="' + esc(t("field.maxRating")) + '"><option value="">' + esc(t("filter.noLimit")) + "</option>" + options(ratings.map(function (r) { return [r.Value, r.Name]; })) + "</select></div>" +
                '<div class="selectContainer"><select is="emby-select" id="svTimeLimit" label="' + esc(t("field.timeLimit")) + '">' + options([1, 3, 5, 10, 15].map(function (m) { return [m, m + " " + t("time.minutes"), m === 5]; })) + "</select></div>" +
                '<div class="selectContainer"><select is="emby-select" id="svSortBy" label="' + esc(t("field.sortBy")) + '">' + options([["Random", t("sort.random")], ["Title", t("sort.title")], ["CommunityRating", t("sort.rating")], ["PremiereDate", t("sort.release")]]) + "</select></div>" +
                "</div>" +
                '<div class="fieldDescription">' + esc(t("info.syncPlayMembers", members)) + "</div>" +
                "</form>";
            setFooter(btn(t("btn.createRoom"), "button-submit svCreate"));

            var warning = body.querySelector("#svAccessWarning");
            var timer = null;
            body.querySelectorAll(".svCollection").forEach(function (box) {
                box.addEventListener("change", function () {
                    clearTimeout(timer);
                    timer = setTimeout(function () {
                        var ids = Array.prototype.map.call(body.querySelectorAll(".svCollection:checked"), function (b) {
                            return b.value;
                        });
                        if (!ids.length) {
                            warning.style.display = "none";
                            return;
                        }
                        call("POST", "SyncVote/CheckAccess", { CollectionIds: ids })
                            .then(function (r) {
                                warning.style.display = r && r.HasAccessIssues ? "" : "none";
                            })
                            .catch(function () {
                                warning.style.display = "none";
                            });
                    }, 400);
                });
            });

            // Errors are shown inside the form: a toast would end up behind the dialog.
            var formError = body.querySelector("#svFormError");
            var showError = function (message, field) {
                formError.textContent = message;
                formError.style.display = "";
                formError.scrollIntoView({ block: "nearest" });
                if (field) {
                    field.focus();
                }
            };
            body.addEventListener("input", function () {
                formError.style.display = "none";
            });
            body.addEventListener("change", function () {
                formError.style.display = "none";
            });

            dialog.footer().querySelector(".svCreate").addEventListener("click", function () {
                var name = body.querySelector("#svRoomName").value.trim();
                if (!name) {
                    showError(t("msg.enterRoomName"), body.querySelector("#svRoomName"));
                    return;
                }
                var selected = Array.prototype.map.call(body.querySelectorAll(".svCollection:checked"), function (b) {
                    return b.value;
                });
                if (!selected.length) {
                    showError(t("msg.selectLibrary"), body.querySelector(".svCollection"));
                    return;
                }
                var rating = body.querySelector("#svMaxRating").value;
                var request = {
                    Name: name,
                    TimeLimit: parseInt(body.querySelector("#svTimeLimit").value, 10) || 5,
                    SortBy: body.querySelector("#svSortBy").value,
                    SelectedCollections: selected,
                    SelectedGenres: Array.prototype.map.call(body.querySelectorAll(".svGenre:checked"), function (b) { return b.value; }),
                    ItemTypes: ["Movie"],
                };
                var titleFilter = body.querySelector("#svTitleFilter").value.trim();
                if (titleFilter) {
                    request.TitleFilter = titleFilter;
                }
                if (rating !== "") {
                    request.MaxParentalRating = parseInt(rating, 10);
                }
                call("POST", "SyncVote/Room", request)
                    .then(function (room) {
                        return self.enterRoom(room);
                    })
                    .catch(function (e) {
                        console.error(LOG, e);
                        showError(e.status === 409 ? t("msg.roomExists") : t("msg.errorCreatingRoom"));
                    });
            });
        });
    };

    // ------------------------------------------------------------------ lobby

    SyncVoteManager.prototype.showLobby = function () {
        var self = this;
        var room = this.room;
        var body = this.openDialog("lobby", t("title") + ": " + room.Name);
        var organizer = this.isOrganizer();
        body.innerHTML =
            '<div style="text-align:center;margin:1.5em 0">' +
            '<span class="material-icons" style="font-size:4em" aria-hidden="true">how_to_vote</span>' +
            '<h2 id="svLobbyMembers" class="sectionTitle"></h2>' +
            '<div class="fieldDescription">' + esc(organizer ? t("lobby.organizerHint") : t("lobby.waiting")) + "</div>" +
            "</div>";
        this.renderLobbyMembers();
        this.startPolling();

        setFooter(organizer ? btn(t("manageVoting.start"), "button-submit svStart", "play_arrow") + btn(t("btn.closeRoom"), "button-cancel svCloseRoom", "delete") : "");
        if (organizer) {
            dialog.footer().querySelector(".svStart").addEventListener("click", function () {
                call("POST", "SyncVote/Room/" + room.Id + "/StartVoting")
                    .then(function (updated) {
                        self.room = updated;
                        self.render();
                    })
                    .catch(function () {
                        toast(t("msg.errorStarting"));
                    });
            });
            dialog.footer().querySelector(".svCloseRoom").addEventListener("click", function () {
                self.closeRoom();
            });
        }
    };

    SyncVoteManager.prototype.renderLobbyMembers = function () {
        var el = document.getElementById("svLobbyMembers");
        if (el && this.room) {
            var inRoom = (this.room.MemberIds || []).length;
            el.textContent = t("lobby.members", inRoom, (this.group && this.group.MemberCount) || inRoom);
        }
    };

    SyncVoteManager.prototype.closeRoom = function () {
        var self = this;
        if (!this.room) {
            return;
        }
        call("DELETE", "SyncVote/Room/" + this.room.Id)
            .catch(function () {
                /* already gone */
            })
            .then(function () {
                self.room = null;
                dialog.close();
            });
    };

    // ----------------------------------------------------------------- voting

    SyncVoteManager.prototype.showVoting = function () {
        var self = this;
        var room = this.room;
        var body = this.openDialog("voting", t("title") + ": " + room.Name);
        var organizer = this.isOrganizer();
        body.innerHTML =
            '<div class="svVoting">' +
            '<h2 class="sectionTitle svTimer"><span id="svTime">0:00</span></h2>' +
            '<div class="svProgress"><div id="svProgressBar" class="svProgressBar"></div></div>' +
            '<div class="svCard" id="svCard">' + skeletonCard() + "</div>" +
            '<div class="fieldDescription" id="svVotesCount" style="text-align:center"></div>' +
            "</div>";
        // The vote buttons live in the footer: it never scrolls, so they are always within reach.
        setFooter(
            '<div class="svVoteButtons">' +
                '<button is="paper-icon-button-light" type="button" class="svVote svDislike" id="svDislike" title="' + esc(t("btn.dislike")) + '"><span class="material-icons thumb_down" aria-hidden="true"></span></button>' +
                '<button is="paper-icon-button-light" type="button" class="svVote svLike" id="svLike" title="' + esc(t("btn.like")) + '"><span class="material-icons thumb_up" aria-hidden="true"></span></button>' +
                "</div>",
        );
        dialog.container.classList.add("svVotingDialog");
        if (organizer) {
            var header = dialog.container.querySelector(".formDialogHeader");
            header.insertAdjacentHTML("beforeend", '<button is="paper-icon-button-light" type="button" class="svEnd autoSize" title="' + esc(t("btn.endVoting")) + '"><span class="material-icons stop_circle" aria-hidden="true"></span></button>');
        }

        dialog.footer().querySelector("#svLike").addEventListener("click", function () {
            self.vote(true);
        });
        dialog.footer().querySelector("#svDislike").addEventListener("click", function () {
            self.vote(false);
        });
        if (organizer) {
            dialog.container.querySelector(".svEnd").addEventListener("click", function () {
                call("POST", "SyncVote/Room/" + room.Id + "/EndVoting")
                    .then(function (updated) {
                        self.room = updated;
                        self.render();
                    })
                    .catch(function () {
                        toast(t("msg.error"));
                    });
            });
        }
        this.enableSwipe(body.querySelector("#svCard"));

        this.remaining = room.SecondsRemaining || 0;
        this.total = Math.max(1, (room.TimeLimit || 1) * 60);
        var last = Date.now();
        this.tickTimer = setInterval(function () {
            var now = Date.now();
            self.remaining = Math.max(0, self.remaining - (now - last) / 1000);
            last = now;
            self.drawTimer();
            if (self.remaining <= 0) {
                clearInterval(self.tickTimer);
                self.tickTimer = null;
                self.refreshRoom();
            }
        }, 250);
        this.startPolling();
        this.drawTimer();
        this.next();
    };

    SyncVoteManager.prototype.drawTimer = function () {
        var time = document.getElementById("svTime");
        var bar = document.getElementById("svProgressBar");
        if (!time || !bar) {
            return;
        }
        var s = Math.ceil(this.remaining);
        time.textContent = Math.floor(s / 60) + ":" + String(s % 60).padStart(2, "0");
        bar.style.width = Math.max(0, Math.min(100, (this.remaining / this.total) * 100)) + "%";
    };

    SyncVoteManager.prototype.enableSwipe = function (card) {
        var self = this;
        var startX = null;
        card.addEventListener("pointerdown", function (e) {
            startX = e.clientX;
        });
        card.addEventListener("pointerup", function (e) {
            if (startX === null) {
                return;
            }
            var dx = e.clientX - startX;
            startX = null;
            if (Math.abs(dx) > 80) {
                self.vote(dx > 0);
            }
        });
        card.addEventListener("pointercancel", function () {
            startX = null;
        });
    };

    document.addEventListener("keydown", function (e) {
        var manager = window.__syncVoteManager;
        if (!manager || manager.view !== "voting" || !dialog.isOpen()) {
            return;
        }
        if (e.key === "ArrowRight") {
            manager.vote(true);
        } else if (e.key === "ArrowLeft") {
            manager.vote(false);
        }
    });

    function posterUrl(item) {
        return client().getImageUrl(item.Id, { type: "Primary", maxWidth: 400 });
    }

    // Loads the poster ahead of time so it is in the browser cache when the card shows up.
    SyncVoteManager.prototype.preload = function (item) {
        this.preloaded = this.preloaded || {};
        if (!this.preloaded[item.Id]) {
            var image = new Image();
            image.src = posterUrl(item);
            this.preloaded[item.Id] = image;
        }
    };

    // Same size as a real card, so nothing moves when the content arrives.
    function skeletonCard() {
        return '<div class="svPosterBox svSkeleton"></div><div class="svCardText"><div class="svSkeletonBar" style="width:70%"></div><div class="svSkeletonBar" style="width:45%"></div></div>';
    }

    SyncVoteManager.prototype.fillQueue = function () {
        var self = this;
        if (this.loading || this.exhausted || !this.room) {
            return Promise.resolve();
        }
        this.loading = true;
        return call("GET", "SyncVote/Room/" + this.room.Id + "/Candidates?skip=0&limit=20")
            .then(function (result) {
                var items = ((result && result.Items) || []).filter(function (item) {
                    return !self.seen[item.Id];
                });
                items.forEach(function (item) {
                    self.seen[item.Id] = true;
                    self.preload(item);
                });
                self.queue = self.queue.concat(items);
                if (!items.length) {
                    self.exhausted = true;
                }
            })
            .catch(function (e) {
                console.error(LOG, e);
            })
            .then(function () {
                self.loading = false;
            });
    };

    SyncVoteManager.prototype.next = function () {
        var self = this;
        var ready = this.queue.length >= 3 ? Promise.resolve() : this.fillQueue();
        return ready.then(function () {
            var card = document.getElementById("svCard");
            if (!card) {
                return;
            }
            self.current = self.queue.shift() || null;
            var count = document.getElementById("svVotesCount");
            if (count) {
                count.textContent = t(self.votes === 1 ? "voting.cast.one" : "voting.cast", self.votes);
            }
            if (!self.current) {
                card.innerHTML = '<div class="svPosterBox"></div><div class="svCardText"><div class="fieldDescription">' + esc(t("voting.noMore")) + "</div></div>";
                return;
            }
            var item = self.current;
            var details = [item.ProductionYear, (item.Genres || []).slice(0, 3).join(", "), item.OfficialRating].filter(Boolean).join(" • ");
            card.innerHTML =
                '<div class="svPosterBox svSkeleton"><img class="svPoster" src="' + esc(posterUrl(item)) + '" alt="" draggable="false"></div>' +
                '<div class="svCardText"><h2 class="sectionTitle svTitle">' + esc(item.Name) + "</h2>" +
                '<div class="fieldDescription">' + esc(details) + "</div>" +
                '<div class="fieldDescription svOverview">' + esc(item.Overview || "") + "</div></div>";
            var box = card.querySelector(".svPosterBox");
            var image = card.querySelector(".svPoster");
            var shown = function () {
                box.classList.add("svLoaded");
            };
            // No poster for this title: a neutral placeholder instead of the browser's broken-image icon.
            var missing = function () {
                image.remove();
                box.insertAdjacentHTML("afterbegin", '<span class="material-icons svNoPoster" aria-hidden="true">movie</span>');
                shown();
            };
            if (image.complete) {
                if (image.naturalWidth > 0) {
                    shown();
                } else {
                    missing();
                }
            } else {
                image.addEventListener("load", shown);
                image.addEventListener("error", missing);
            }
            self.queue.slice(0, 3).forEach(function (upcoming) {
                self.preload(upcoming);
            });
            if (self.queue.length < 3) {
                self.fillQueue();
            }
        });
    };

    SyncVoteManager.prototype.vote = function (isLike) {
        var self = this;
        var item = this.current;
        if (!item || this.view !== "voting" || this.voting) {
            return;
        }
        this.voting = true;
        call("POST", "SyncVote/Vote", { RoomId: this.room.Id, ItemId: item.Id, IsLike: isLike })
            .then(function () {
                self.votes++;
                self.current = null;
                return self.next();
            })
            .catch(function (e) {
                if (e.status === 400) {
                    // The round ended while the card was on screen.
                    self.refreshRoom();
                } else {
                    console.error(LOG, e);
                    toast(t("msg.error"));
                }
            })
            .then(function () {
                self.voting = false;
            });
    };

    // ---------------------------------------------------------------- results

    SyncVoteManager.prototype.showResults = function () {
        var self = this;
        var room = this.room;
        var body = this.openDialog("results", t("results.title") + ": " + room.Name);
        var organizer = this.isOrganizer();
        body.innerHTML = '<p class="fieldDescription">' + esc(t("msg.loading")) + "</p>";
        setFooter("");
        this.startPolling();

        return call("GET", "SyncVote/Room/" + room.Id + "/Results")
            .then(function (results) {
                if (self.view !== "results") {
                    return;
                }
                var html = "";
                if (!results.Winner) {
                    html = '<div class="fieldDescription" style="text-align:center">' + esc(t("results.none")) + "</div>";
                } else {
                    var winner = results.Winner;
                    html += '<h2 class="sectionTitle">' + esc(t("results.winner")) + "</h2>";
                    html += listItem("emoji_events", winner.Name + (winner.Year ? " (" + winner.Year + ")" : ""), votes(winner.VoteCount));
                    if (organizer && results.MembersWithoutAccess > 0) {
                        html += '<div class="fieldDescription" style="color:#ffc107"><span class="material-icons" style="vertical-align:middle">warning</span> ' + esc(t("results.accessIssue", results.MembersWithoutAccess)) + "</div>";
                    }
                    if (results.LikedItems.length > 1) {
                        html += '<h2 class="sectionTitle">' + esc(t("results.others")) + "</h2>";
                        html += results.LikedItems.slice(1, 11)
                            .map(function (i) {
                                return listItem("thumb_up", i.Name, votes(i.VoteCount));
                            })
                            .join("");
                    }
                }
                body.innerHTML = html;

                setFooter(organizer ? (results.Winner ? btn(t("btn.playWinner"), "button-submit svPlay", "play_arrow") : "") + btn(t("btn.newRound"), "svAgain", "replay") + btn(t("btn.closeRoom"), "button-cancel svCloseRoom", "delete") : "");
                if (!organizer) {
                    return;
                }
                var footer = dialog.footer();
                var play = footer.querySelector(".svPlay");
                if (play) {
                    play.addEventListener("click", function () {
                        play.disabled = true;
                        call("POST", "SyncVote/Room/" + room.Id + "/PlayWinner")
                            .then(function () {
                                dialog.close();
                                toast(t("play.starting"));
                            })
                            .catch(function () {
                                play.disabled = false;
                                toast(t("play.error"));
                            });
                    });
                }
                footer.querySelector(".svAgain").addEventListener("click", function () {
                    call("POST", "SyncVote/Room/" + room.Id + "/StartVoting")
                        .then(function (updated) {
                            self.room = updated;
                            self.queue = [];
                            self.seen = {};
                            self.votes = 0;
                            self.exhausted = false;
                            self.render();
                        })
                        .catch(function () {
                            toast(t("msg.errorStarting"));
                        });
                });
                footer.querySelector(".svCloseRoom").addEventListener("click", function () {
                    self.closeRoom();
                });
            })
            .catch(function (e) {
                console.error(LOG, e);
                body.innerHTML = '<div class="fieldDescription">' + esc(t("msg.error")) + "</div>";
            });
    };

    // ------------------------------------------------------------------- boot

    function init() {
        if (window.__syncVoteManager) {
            return;
        }
        if (!client() || !document.body) {
            setTimeout(init, 300);
            return;
        }
        window.__syncVoteManager = new SyncVoteManager();
        window.SyncVote = {
            open: function () {
                return window.__syncVoteManager.openPanel();
            },
        };
        window.__syncVoteManager.start();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
