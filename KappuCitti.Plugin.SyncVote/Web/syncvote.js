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
                // The body of a refusal says why ({ Error: "WrongCode", RetryAfterSeconds: 30 }).
                return response.text().then(function (text) {
                    var error = new Error(method + " " + path + " -> " + response.status);
                    error.status = response.status;
                    try {
                        error.body = text ? JSON.parse(text) : null;
                    } catch (ignored) {
                        error.body = null;
                    }
                    throw error;
                });
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
    function btn(label, cls, icon, hint) {
        return '<button is="emby-button" type="button" class="raised block formDialogFooterItem ' + cls + '"' + (hint ? ' title="' + esc(hint) + '"' : "") + ">" + (icon ? '<span class="material-icons ' + icon + '" aria-hidden="true"></span>' : "") + "<span>" + esc(label) + "</span></button>";
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
        setInterval(function () {
            if (!document.hidden) {
                self.updateBadge();
            }
        }, BADGE_POLL_MS);
        console.log(LOG, "ready");
    };

    // ----------------------------------------------------- SyncPlay badge

    var BADGE_POLL_MS = 5000;

    // The SyncPlay button of the new layout wears a dot while there is a group to join or a group joined. It is marked (and turned
    // purple by the stylesheet) while the group is voting: from the start of a round until its time is up or the organizer ends it.
    // The mark is an attribute of the button, which the web client does not manage, so its own re-renders leave it alone.
    SyncVoteManager.prototype.updateBadge = function () {
        var icon = document.querySelector('.MuiAppBar-root svg[data-testid="GroupsIcon"]');
        var button = icon ? icon.closest("button") : null;
        if (!button) {
            return Promise.resolve();
        }
        var dot = button.querySelector(".MuiBadge-badge");
        if (!dot || dot.classList.contains("MuiBadge-invisible")) {
            button.removeAttribute("data-sv-room");
            return Promise.resolve();
        }
        return call("GET", "SyncVote/SyncPlayInfo")
            .then(function (info) {
                if (info && info.GroupId && info.RoomState === "voting") {
                    button.setAttribute("data-sv-room", "voting");
                } else {
                    button.removeAttribute("data-sv-room");
                }
            })
            .catch(function () {
                /* keep what is shown until the next check */
            });
    };

    SyncVoteManager.prototype.currentUserId = function () {
        var api = client();
        return Promise.resolve(api && api.getCurrentUserId ? api.getCurrentUserId() : null);
    };

    // Administrators can close any room (the server checks it); the client only has to show them the button.
    SyncVoteManager.prototype.currentIsAdmin = function () {
        var api = client();
        return Promise.resolve(api && api.getCurrentUser ? api.getCurrentUser() : null)
            .then(function (user) {
                return !!(user && user.Policy && user.Policy.IsAdministrator);
            })
            .catch(function () {
                return false;
            });
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

        // The Material UI menu of the new layout (12, and "experimental" in 10.11).
        document.querySelectorAll('ul[role="menu"]').forEach(function (list) {
            var existing = list.querySelector(".svSyncVoteItem");
            var first = firstNativeItem(list);

            // The web client keeps this menu in the page (also when closed) and rebuilds its entries when the group changes, the
            // playback starts or stops, and so on. It does not know our entry, so the entry has to follow: gone when this is not
            // the menu of a group, and always the first of the native ones, under the header of the group.
            if (!isGroupMenu(list)) {
                if (existing) {
                    existing.remove();
                }
                return;
            }
            if (existing) {
                if (existing.nextElementSibling !== first && !(first === null && list.lastElementChild === existing)) {
                    list.insertBefore(existing, first);
                }
                return;
            }

            var template = first;
            var item;
            if (template && template.querySelector(".MuiListItemIcon-root") && template.querySelector(".MuiListItemText-primary")) {
                // A copy of one of Jellyfin's own entries: its classes are generated by the web client, so the copy looks like the
                // others in any version. Only the icon and the text change.
                item = template.cloneNode(true);
                item.querySelector(".MuiListItemIcon-root").innerHTML = '<span class="material-icons svMenuIcon" aria-hidden="true">how_to_vote</span>';
                item.querySelector(".MuiListItemText-primary").textContent = t("menu.open");
            } else {
                item = document.createElement("li");
                item.className = "MuiButtonBase-root MuiMenuItem-root MuiMenuItem-gutters";
                item.style.cssText = "display:flex;align-items:center;gap:12px;padding:6px 16px;cursor:pointer;";
                item.innerHTML = '<span class="material-icons svMenuIcon" aria-hidden="true">how_to_vote</span><span>' + esc(t("menu.open")) + "</span>";
            }
            item.classList.add("svSyncVoteItem");
            item.setAttribute("role", "menuitem");
            item.setAttribute("tabindex", "-1");
            item.addEventListener("click", function (e) {
                e.preventDefault();
                e.stopPropagation();
                var backdrop = (list.closest(".MuiPopover-root, .MuiMenu-root") || document).querySelector(".MuiBackdrop-root");
                if (backdrop) {
                    backdrop.click();
                }
                sleep(250).then(function () {
                    return self.openPanel();
                });
            });
            // The web client does not know this entry, so the keyboard needs help: arrows reach it, Enter and Space pick it.
            item.addEventListener("keydown", function (e) {
                if (e.key === "Enter" || e.key === " ") {
                    e.preventDefault();
                    e.stopPropagation();
                    item.click();
                }
            });
            // First in the list: it is what the menu is opened for, and "leave group" stays last where it was.
            if (template) {
                list.insertBefore(item, template);
            } else {
                list.appendChild(item);
            }
        });
    };

    // The first entry of the menu that belongs to the web client (ours is not counted).
    function firstNativeItem(list) {
        return (
            Array.from(list.children).find(function (child) {
                return child.matches('li[role="menuitem"]') && !child.classList.contains("svSyncVoteItem");
            }) || null
        );
    }

    // The list of a SyncPlay group, as opposed to the one that only offers to create or join a group. The header of a group has an id
    // and the "leave group" button is only an icon in 12, so neither depends on a language; the words are a last resort.
    function isGroupMenu(list) {
        return !!list.querySelector('#sync-play-active-subheader, [data-testid="PersonRemoveIcon"]') || /leave group|lascia il gruppo/i.test(list.textContent || "");
    }

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
                return Promise.all([self.currentUserId(), call("GET", "SyncVote/SyncPlayInfo"), self.currentIsAdmin()]);
            })
            .then(function (result) {
                self.userId = result[0];
                self.isAdmin = result[2];
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

    SyncVoteManager.prototype.canClose = function () {
        return this.isOrganizer() || (!!this.room && !!this.isAdmin);
    };

    SyncVoteManager.prototype.stateOf = function (room) {
        if (!room) {
            return "none";
        }
        return room.IsVotingActive ? "voting" : room.IsVotingFinished ? "results" : "lobby";
    };

    SyncVoteManager.prototype.isMember = function (room) {
        var self = this;
        return !!room && (room.MemberIds || []).some(function (id) {
            return same(id, self.userId);
        });
    };

    // A room with a code lets in only the people who give it (administrators do not need it): until then there is nothing to vote on.
    SyncVoteManager.prototype.needsCode = function () {
        return !!this.room && !!this.room.RequiresCode && !this.isAdmin && !this.isMember(this.room);
    };

    // Gets into the room. Joining is tried without a code first: it works for a room without one, and for somebody who already
    // gave the code of this SyncPlay session; otherwise the screen asks for it.
    SyncVoteManager.prototype.enterRoom = function (room) {
        var self = this;
        var joined = this.isMember(room) ? Promise.resolve(room) : call("POST", "SyncVote/Room/" + room.Id + "/Join");
        return joined
            .then(function (updated) {
                self.beginRoom(updated || room);
            })
            .catch(function (e) {
                if (e.status === 403 && e.body && e.body.Error === "CodeRequired") {
                    self.room = room;
                    self.render();
                    self.startPolling();
                    return;
                }
                throw e;
            });
    };

    SyncVoteManager.prototype.beginRoom = function (room) {
        this.room = room;
        this.queue = [];
        this.seen = {};
        this.current = null;
        this.votes = 0;
        this.exhausted = false;
        this.render();
        this.startPolling();
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
                // Out of the room (a new round with a new code) or let in (somebody gave the code in another tab): the screen follows.
                if (self.needsCode() !== (self.view === "code")) {
                    self.render();
                } else if (self.view === "code") {
                    return;
                } else if (self.stateOf(room) !== before) {
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
        this.updateBadge();
        if (this.needsCode()) {
            this.showCodePrompt();
            return;
        }
        var state = this.stateOf(this.room);
        if (state === "voting") {
            this.showVoting();
        } else if (state === "results") {
            this.showResults();
        } else {
            this.showLobby();
        }
    };

    // "SyncVote: Movie night (runoff)": a runoff only has the titles liked in the previous round.
    SyncVoteManager.prototype.roomTitle = function (prefix) {
        var room = this.room;
        return prefix + ": " + room.Name + (room.IsRunoff ? " (" + t("round.runoff") + ")" : "");
    };

    // Starts the next round and shows it. A runoff votes again only on the titles that got a like.
    SyncVoteManager.prototype.startRound = function (onlyLiked) {
        var self = this;
        return call("POST", "SyncVote/Room/" + this.room.Id + "/StartVoting" + (onlyLiked ? "?onlyLiked=true" : ""))
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
    };

    SyncVoteManager.prototype.openDialog = function (view, title) {
        var self = this;
        this.view = view;
        this.stopTimers();
        var body = dialog.open(title, function () {
            self.view = null;
            self.stopTimers();
        });
        // The organizer (and the administrators) have the code of the room in front of them all the time.
        if (view !== "create" && this.room && this.room.Code) {
            this.addCodeChip(this.room.Code);
        }
        return body;
    };

    // "482913" -> "482 913"
    function formatCode(code) {
        return String(code).replace(/^(\d{3})(\d+)$/, "$1 $2");
    }

    // Copies text where the browser lets it (the clipboard API only works on https, so there is a fallback for plain http).
    function copyText(text) {
        if (navigator.clipboard && window.isSecureContext) {
            return navigator.clipboard.writeText(text);
        }
        return new Promise(function (resolve, reject) {
            var area = document.createElement("textarea");
            area.value = text;
            area.style.cssText = "position:fixed;left:-1000px;top:0;opacity:0";
            document.body.appendChild(area);
            area.select();
            try {
                document.execCommand("copy") ? resolve() : reject(new Error("copy refused"));
            } catch (e) {
                reject(e);
            } finally {
                area.remove();
            }
        });
    }

    // The code in the header, with what the organizer (or an administrator) can do about it: copy it, or make a new one when it got
    // to somebody who should not have it. A new code puts everybody else out of the room, so it asks first (inline: the
    // dialogs of the browser are not always shown in the apps built on the web client).
    SyncVoteManager.prototype.addCodeChip = function (code) {
        var self = this;
        var room = this.room;
        var header = dialog.container.querySelector(".formDialogHeader");
        header.insertAdjacentHTML("beforeend", '<span class="svCodeTools"></span>');
        var tools = header.querySelector(".svCodeTools");
        var canRenew = this.canClose();
        var timer = null;

        var show = function () {
            clearTimeout(timer);
            if (!document.body.contains(tools)) {
                return;
            }
            tools.innerHTML =
                '<button type="button" class="svCode" title="' + esc(t("code.chip.title")) + '"><span class="material-icons" aria-hidden="true">vpn_key</span><span class="svCodeText">' + esc(formatCode(code)) + "</span></button>" +
                (canRenew ? '<button type="button" class="svCodeRenew" title="' + esc(t("code.renew.title")) + '"><span class="material-icons" aria-hidden="true">autorenew</span></button>' : "");
            tools.querySelector(".svCode").addEventListener("click", function () {
                copyText(code).then(
                    function () {
                        toast(t("code.copied"));
                    },
                    function () {
                        toast(formatCode(code));
                    },
                );
            });
            if (canRenew) {
                tools.querySelector(".svCodeRenew").addEventListener("click", ask);
            }
        };

        var renew = function () {
            clearTimeout(timer);
            call("POST", "SyncVote/Room/" + room.Id + "/NewCode")
                .then(function (updated) {
                    self.room = updated;
                    // Not a full render: the card on screen stays where it is.
                    tools.remove();
                    self.addCodeChip(updated.Code);
                    var big = document.getElementById("svCodeBig");
                    if (big) {
                        big.textContent = formatCode(updated.Code);
                    }
                    toast(t("code.renewed"));
                })
                .catch(function (e) {
                    console.error(LOG, e);
                    toast(t("msg.error"));
                    show();
                });
        };

        var ask = function () {
            tools.innerHTML =
                '<span class="svCodeAsk">' + esc(t("code.renew.ask")) + "</span>" +
                '<button type="button" class="svCodeYes" title="' + esc(t("code.renew.yes")) + '"><span class="material-icons" aria-hidden="true">check</span></button>' +
                '<button type="button" class="svCodeNo" title="' + esc(t("code.renew.no")) + '"><span class="material-icons" aria-hidden="true">close</span></button>';
            tools.querySelector(".svCodeYes").addEventListener("click", renew);
            tools.querySelector(".svCodeNo").addEventListener("click", show);
            timer = setTimeout(show, 6000);
        };

        show();
    };
    // ------------------------------------------------------------- room code

    // Shown to somebody who is not in a room that asks for a code: nothing to vote on until the right one is typed.
    SyncVoteManager.prototype.showCodePrompt = function () {
        var self = this;
        var room = this.room;
        var body = this.openDialog("code", t("title") + ": " + room.Name);
        body.innerHTML =
            '<div class="svCodePrompt">' +
            '<span class="material-icons" style="font-size:4em" aria-hidden="true">lock</span>' +
            '<h2 class="sectionTitle">' + esc(t("code.prompt.title")) + "</h2>" +
            '<div class="fieldDescription">' + esc(t("code.prompt.desc")) + "</div>" +
            '<div id="svCodeError" class="svError" role="alert" style="display:none"></div>' +
            '<div class="inputContainer"><input is="emby-input" type="text" id="svCodeInput" class="svCodeInput" inputmode="numeric" autocomplete="one-time-code" maxlength="8" label="' + esc(t("field.code")) + '"></div>' +
            "</div>";
        setFooter(btn(t("btn.enterCode"), "button-submit svEnterCode", "lock_open"));
        this.startPolling();

        var input = body.querySelector("#svCodeInput");
        var error = body.querySelector("#svCodeError");
        var submit = dialog.footer().querySelector(".svEnterCode");
        var fail = function (message) {
            error.textContent = message;
            error.style.display = "";
            input.focus();
        };
        var join = function () {
            var code = input.value.replace(/\D/g, "");
            if (!code) {
                fail(t("msg.enterCode"));
                return;
            }
            submit.disabled = true;
            call("POST", "SyncVote/Room/" + room.Id + "/Join", { Code: code })
                .then(function (updated) {
                    self.beginRoom(updated);
                })
                .catch(function (e) {
                    submit.disabled = false;
                    var reason = e.body && e.body.Error;
                    if (e.status === 429) {
                        fail(t("msg.tooManyAttempts", (e.body && e.body.RetryAfterSeconds) || 30));
                    } else if (e.status === 403 && reason === "WrongCode") {
                        input.select();
                        fail(t("msg.wrongCode"));
                    } else if (e.status === 404) {
                        self.refreshRoom();
                    } else {
                        console.error(LOG, e);
                        fail(t("msg.error"));
                    }
                });
        };
        submit.addEventListener("click", join);
        input.addEventListener("keydown", function (e) {
            if (e.key === "Enter") {
                e.preventDefault();
                join();
            }
        });
        input.addEventListener("input", function () {
            error.style.display = "none";
        });
        setTimeout(function () {
            if (document.body.contains(input)) {
                input.focus();
            }
        }, 150);
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
                // The option shares a row with the line about the group, so that the form is not taller than it was; what it needs
                // once it is on shows up only then.
                '<div class="svCodeRow">' +
                '<label class="emby-checkbox-label"><input type="checkbox" is="emby-checkbox" id="svRequireCode"><span class="checkboxLabel">' + esc(t("field.requireCode")) + "</span></label>" +
                '<div class="fieldDescription">' + esc(t("info.syncPlayMembers", members)) + "</div>" +
                "</div>" +
                '<div id="svCodeScopeBox" style="display:none">' +
                '<div class="fieldDescription">' + esc(t("field.requireCode.desc")) + "</div>" +
                '<div class="selectContainer"><select is="emby-select" id="svCodeScope" label="' + esc(t("field.codeScope")) + '">' + options([["Session", t("code.scope.session")], ["Round", t("code.scope.round")]]) + "</select></div>" +
                "</div>" +
                "</form>";
            body.querySelector("#svRequireCode").addEventListener("change", function () {
                body.querySelector("#svCodeScopeBox").style.display = this.checked ? "" : "none";
            });
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
                if (body.querySelector("#svRequireCode").checked) {
                    request.RequireCode = true;
                    request.CodeScope = body.querySelector("#svCodeScope").value;
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
            // The code, for the organizer and the administrators: this is what the others are asked for.
            (room.Code
                ? '<div class="svCodeBig" id="svCodeBig">' + esc(formatCode(room.Code)) + "</div>" +
                  '<div class="fieldDescription">' + esc(t("code.lobbyHint")) + " " + esc(room.CodeScope === "Round" || room.CodeScope === 1 ? t("code.scope.round.hint") : t("code.scope.session.hint")) + "</div>"
                : "") +
            "</div>";
        this.renderLobbyMembers();
        this.startPolling();

        var canClose = this.canClose();
        setFooter((organizer ? btn(t("manageVoting.start"), "button-submit svStart", "play_arrow") : "") + (canClose ? btn(t("btn.closeRoom"), "button-cancel svCloseRoom", "delete") : ""));
        if (canClose) {
            dialog.footer().querySelector(".svCloseRoom").addEventListener("click", function () {
                self.closeRoom();
            });
        }
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
            .catch(function (e) {
                if (e.status !== 404) {
                    throw e; // refused or unreachable: the room is still there, so say so and keep the dialog
                }
                /* already gone */
            })
            .then(
                function () {
                    self.room = null;
                    dialog.close();
                    self.updateBadge();
                },
                function (e) {
                    console.error(LOG, e);
                    toast(t("msg.errorClosing"));
                },
            );
    };

    // ----------------------------------------------------------------- voting

    SyncVoteManager.prototype.showVoting = function () {
        var self = this;
        var room = this.room;
        var body = this.openDialog("voting", this.roomTitle(t("title")));
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

    var SWIPE_DISTANCE = 80;
    var FLY_MS = 230;

    function reducedMotion() {
        return !!(window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches);
    }

    // Only the poster moves; the text under it stays where it is and changes with the next title.
    function mover(card) {
        return card.querySelector(".svPosterBox");
    }

    // Puts the poster back in place, with or without the spring-back animation.
    function resetCard(card, animate) {
        if (!card) {
            return;
        }
        delete card.dataset.drag;
        var poster = mover(card);
        if (!poster) {
            return;
        }
        // A short swipe that does not make it comes back with a small bounce.
        poster.style.transition = animate && !reducedMotion() ? "transform 0.35s cubic-bezier(0.2, 1.4, 0.4, 1), opacity 0.2s ease-out" : "none";
        poster.style.transform = "";
        poster.style.opacity = "";
        if (!animate) {
            void poster.offsetWidth; // apply the reset before the transition comes back
            poster.style.transition = "";
        }
    }

    // The poster leaves to the side of the vote. Resolves when it is gone.
    function flyOut(card, isLike) {
        var poster = card && mover(card);
        if (!poster || reducedMotion()) {
            return Promise.resolve();
        }
        card.dataset.drag = isLike ? "like" : "dislike";
        card.classList.remove("svEnter"); // a vote right after the poster arrived must not wait for its entrance animation
        var dragged =/translateX\((-?[\d.]+)px\) rotate\((-?[\d.]+)deg\)/.exec(poster.style.transform);
        if (dragged) {
            // Swiped by hand: it is already out of place and tilted, however far it was thrown. It carries on a little in the same
            // direction while it fades; flying to a fixed spot would bring a far-thrown poster back before it disappears.
            var push = (isLike ? 1 : -1) * poster.offsetWidth * 0.25;
            poster.style.transition = "transform " + FLY_MS + "ms ease-out, opacity " + FLY_MS + "ms ease-out";
            poster.style.transform = "translateX(" + (parseFloat(dragged[1]) + push) + "px) rotate(" + parseFloat(dragged[2]) * 1.3 + "deg)";
        } else {
            poster.style.transition = "transform " + FLY_MS + "ms ease-in, opacity " + FLY_MS + "ms ease-in";
            poster.style.transform = "translateX(" + (isLike ? "" : "-") + "120%) rotate(" + (isLike ? 18 : -18) + "deg)";
        }
        poster.style.opacity = "0";
        return new Promise(function (resolve) {
            setTimeout(resolve, FLY_MS);
        });
    }

    SyncVoteManager.prototype.enableSwipe = function (card) {
        var self = this;
        var startX = null;
        var dragging = false;
        card.addEventListener("pointerdown", function (e) {
            // Nothing to swipe while a vote is on its way, or when the titles are over.
            if (self.voting || !self.current) {
                return;
            }
            // While the entrance animation of the poster runs it holds its transform and opacity, so a quick swipe would not show.
            card.classList.remove("svEnter");
            startX = e.clientX;
            dragging = false;
            try {
                card.setPointerCapture(e.pointerId);
            } catch (ignored) {
                /* the swipe still works inside the card */
            }
        });
        // The card follows the finger and shows what the vote will be once it is past the threshold.
        card.addEventListener("pointermove", function (e) {
            if (startX === null) {
                return;
            }
            if (e.buttons === 0) {
                // Moving with nothing pressed: the release was lost (another script or an extension, such as one for mouse gestures,
                // swallowed it). It is not a drag, so do not keep following the pointer.
                startX = null;
                resetCard(card, true);
                return;
            }
            var dx = e.clientX - startX;
            if (!dragging && Math.abs(dx) < 6) {
                return;
            }
            dragging = true;
            var poster = mover(card);
            if (!poster) {
                return;
            }
            poster.style.transition = "none";
            poster.style.transform = "translateX(" + dx + "px) rotate(" + dx / 25 + "deg)";
            if (Math.abs(dx) > SWIPE_DISTANCE) {
                card.dataset.drag = dx > 0 ? "like" : "dislike";
            } else {
                delete card.dataset.drag;
            }
        });
        card.addEventListener("pointerup", function (e) {
            if (startX === null) {
                return;
            }
            var dx = e.clientX - startX;
            startX = null;
            if (Math.abs(dx) > SWIPE_DISTANCE && !self.voting && self.current) {
                self.vote(dx > 0);
            } else {
                resetCard(card, true);
            }
        });
        var abandon = function () {
            if (startX !== null) {
                startX = null;
                resetCard(card, true);
            }
        };
        card.addEventListener("pointercancel", abandon);
        card.addEventListener("lostpointercapture", abandon);
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
            resetCard(card, false);
            // The poster below is a new element, so its entrance animation starts by itself once the class is on the card.
            card.classList.add("svEnter");
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
        var card = document.getElementById("svCard");
        // The card flies away while the vote is sent; the next one appears when both are done.
        Promise.all([call("POST", "SyncVote/Vote", { RoomId: this.room.Id, ItemId: item.Id, IsLike: isLike }), flyOut(card, isLike)])
            .then(function () {
                self.votes++;
                self.current = null;
                return self.next();
            })
            .catch(function (e) {
                if (e.status === 400) {
                    // The round ended while the card was on screen.
                    self.refreshRoom();
                } else if (e.status === 403) {
                    // Not in the room any more (a new round has a new code): the screen asking for it shows up. Anything else is an error.
                    resetCard(card, true);
                    self.refreshRoom().then(function () {
                        if (!self.needsCode()) {
                            toast(t("msg.error"));
                        }
                    });
                } else {
                    console.error(LOG, e);
                    resetCard(card, true);
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
        var body = this.openDialog("results", this.roomTitle(t("results.title")));
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

                // A runoff is only worth it when there is something to choose between: at least two titles got a like.
                var canRunoff = results.LikedItems.length >= 2;
                var canClose = self.canClose();
                setFooter(
                    (organizer
                        ? (results.Winner ? btn(t("btn.playWinner"), "button-submit svPlay", "play_arrow") : "") +
                          (canRunoff ? btn(t("btn.runoff"), "svRunoff", "how_to_vote", t("btn.runoff.hint", results.LikedItems.length)) : "") +
                          btn(t("btn.newRound"), "svAgain", "replay")
                        : "") + (canClose ? btn(t("btn.closeRoom"), "button-cancel svCloseRoom", "delete") : ""),
                );
                var footer = dialog.footer();
                if (canClose) {
                    footer.querySelector(".svCloseRoom").addEventListener("click", function () {
                        self.closeRoom();
                    });
                }
                if (!organizer) {
                    return;
                }
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
                if (canRunoff) {
                    footer.querySelector(".svRunoff").addEventListener("click", function () {
                        self.startRound(true);
                    });
                }
                footer.querySelector(".svAgain").addEventListener("click", function () {
                    self.startRound(false);
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
