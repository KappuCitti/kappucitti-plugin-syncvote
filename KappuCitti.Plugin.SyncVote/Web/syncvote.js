// SyncVote Plugin JavaScript for Jellyfin Web Interface

class SyncVoteManager {
    constructor() {
        this.apiClient = window.ApiClient;
        this.currentRoom = null;
        this.votingTimer = null;
        this.init();
    }

    init() {
        // Add SyncVote button to SyncPlay interface
        this.addSyncVoteButton();

        // Listen for SyncPlay events
        document.addEventListener("syncplay-group-joined", (e) => {
            this.onSyncPlayGroupJoined(e.detail);
        });
    }

    addSyncVoteButton() {
        // This would be injected into the SyncPlay UI
        const syncPlayContainer = document.querySelector(".syncPlayContainer");
        if (syncPlayContainer && !document.querySelector(".syncVoteButton")) {
            const button = document.createElement("button");
            button.className = "syncVoteButton raised button-submit";
            button.innerHTML = '<i class="material-icons">how_to_vote</i><span>Start Voting</span>';
            button.onclick = () => this.showVotingDialog();

            syncPlayContainer.appendChild(button);
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
                this.showJoinVotingButton();
            }
        } catch (error) {
            console.error("Error checking for existing voting rooms:", error);
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

    async showVotingDialog() {
        // Create voting room creation dialog
        const dialogOptions = {
            title: "Create Voting Room",
            text: this.getVotingDialogHtml(),
            html: true,
            buttons: [
                {
                    name: "Cancel",
                    id: "cancel",
                    type: "cancel",
                },
                {
                    name: "Create Room",
                    id: "create",
                    type: "submit",
                },
            ],
        };

        const dialog = await Dashboard.alert(dialogOptions);

        if (dialog === "create") {
            await this.createVotingRoom();
        }
    }

    getVotingDialogHtml() {
        return `
            <div class="inputContainer">
                <label for="roomName">Room Name:</label>
                <input type="text" id="roomName" class="emby-input" placeholder="Enter room name..." required>
            </div>
            <div class="inputContainer">
                <label for="timeLimit">Time Limit:</label>
                <select id="timeLimit" class="emby-select">
                    <option value="1">1 minute</option>
                    <option value="3">3 minutes</option>
                    <option value="5" selected>5 minutes</option>
                    <option value="10">10 minutes</option>
                    <option value="15">15 minutes</option>
                </select>
            </div>
            <div class="inputContainer">
                <label for="sortBy">Sort By:</label>
                <select id="sortBy" class="emby-select">
                    <option value="Random" selected>Random</option>
                    <option value="A-Z">A-Z</option>
                    <option value="Z-A">Z-A</option>
                    <option value="Rating">Rating</option>
                    <option value="DateAdded">Date Added</option>
                </select>
            </div>
        `;
    }

    async createVotingRoom() {
        const roomName = document.getElementById("roomName").value;
        const timeLimit = parseInt(document.getElementById("timeLimit").value);
        const sortBy = document.getElementById("sortBy").value;

        if (!roomName) {
            Dashboard.alert("Please enter a room name");
            return;
        }

        try {
            // Get current SyncPlay group ID (this would come from SyncPlay API)
            const syncPlayGroupId = this.getCurrentSyncPlayGroupId();

            const room = await this.apiClient.ajax({
                type: "POST",
                url: this.apiClient.getUrl("SyncVote/Room"),
                data: JSON.stringify({
                    Name: roomName,
                    SyncPlayGroupId: syncPlayGroupId,
                    TimeLimit: timeLimit,
                    SortBy: sortBy,
                    SelectedCollections: [], // Would be populated from UI
                    SelectedGenres: [], // Would be populated from UI
                }),
                contentType: "application/json",
            });

            this.currentRoom = room;
            Dashboard.alert("Voting room created successfully!");
            this.showVotingInterface();
        } catch (error) {
            console.error("Error creating voting room:", error);
            Dashboard.alert("Error creating voting room");
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
            Dashboard.alert("Error joining voting room");
        }
    }

    showVotingInterface() {
        // Create and show the voting interface
        const votingHtml = this.getVotingInterfaceHtml();

        const dialogOptions = {
            title: `Voting: ${this.currentRoom.Name}`,
            text: votingHtml,
            html: true,
            size: "large",
            buttons: [],
        };

        Dashboard.alert(dialogOptions);
        this.initializeVotingInterface();
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
        // In real implementation, this would fetch filtered movies from Jellyfin API
        // For now, we'll simulate with sample data
        this.currentMovie = this.getNextMovie();

        if (this.currentMovie) {
            document.getElementById("movieTitle").textContent = this.currentMovie.Name;
            document.getElementById("movieDetails").textContent = `${this.currentMovie.ProductionYear} • ${this.currentMovie.Genres?.join(", ") || "Unknown"}`;
            document.getElementById("movieImage").src = this.apiClient.getImageUrl(this.currentMovie.Id, { type: "Primary", maxWidth: 300 });
        }
    }

    getNextMovie() {
        // Placeholder - would fetch from Jellyfin library based on room filters
        return {
            Id: Guid.newGuid(),
            Name: "Sample Movie",
            ProductionYear: 2023,
            Genres: ["Action", "Adventure"],
        };
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
            title: "Voting Results",
            text: resultsHtml,
            html: true,
            size: "large",
            buttons: [
                {
                    name: "Play Winner",
                    id: "play",
                    type: "submit",
                },
                {
                    name: "Close",
                    id: "close",
                    type: "cancel",
                },
            ],
        };

        Dashboard.alert(dialogOptions).then((result) => {
            if (result === "play" && results.Winner) {
                this.playWinner(results.Winner);
            }
        });
    }

    getResultsHtml(results) {
        if (!results.Winner) {
            return "<p>No movies received enough votes to win.</p>";
        }

        let html = `
            <div class="votingResults">
                <div class="winner">
                    <h3>🏆 Winner: ${results.Winner.Name}</h3>
                    <p>${results.Winner.VoteCount} votes</p>
                </div>
        `;

        if (results.LikedItems.length > 1) {
            html += '<div class="otherResults"><h4>Other liked movies:</h4><ul>';
            results.LikedItems.slice(1).forEach((item) => {
                html += `<li>${item.Name} (${item.VoteCount} votes)</li>`;
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

            Dashboard.alert("Starting playback of the winning movie!");
        } catch (error) {
            console.error("Error starting playback:", error);
            Dashboard.alert("Error starting playback");
        }
    }

    getCurrentSyncPlayGroupId() {
        // This would get the current SyncPlay group ID from the SyncPlay API
        // For demo purposes, return a placeholder
        return "demo-group-id";
    }
}

// Initialize SyncVote when the page loads
document.addEventListener("DOMContentLoaded", () => {
    if (window.ApiClient) {
        new SyncVoteManager();
    }
});
