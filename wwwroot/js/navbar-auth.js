(function () {
    function roleToDisplayName(role) {
        switch ((role || "").toLowerCase()) {
            case "admin":
                return "Quản trị viên";
            case "student":
                return "Người tìm kiếm";
            case "host":
                return "Chủ trọ";
            case "employer":
                return "Nhà tuyển dụng";
            default:
                return "Người dùng";
        }
    }

    function createNavbarAuth(options) {
        const clearStoredAuth = options.clearStoredAuth;
        const resetNotifications = options.resetNotifications;
        const stopNotifications = options.stopNotifications;

        function ensureHostAppointmentsItem() {
            const userMenu = document.querySelector("#nav-auth-user-item .auth-user-menu");
            if (!userMenu) return null;

            let item = document.getElementById("nav-host-appointments-link");
            if (item) return item;

            const divider = userMenu.querySelector(".dropdown-divider");
            item = document.createElement("li");
            item.id = "nav-host-appointments-link";
            item.className = "d-none";
            item.innerHTML = '<a class="dropdown-item" href="/Home/HostAppointments"><i class="fas fa-calendar-check me-2"></i>Yêu cầu xem phòng</a>';

            if (divider && divider.parentElement) {
                divider.parentElement.insertBefore(item, divider);
            } else {
                userMenu.appendChild(item);
            }

            return item;
        }

        function ensureEmployerApplicationsItem() {
            const userMenu = document.querySelector("#nav-auth-user-item .auth-user-menu");
            if (!userMenu) return null;

            let item = document.getElementById("nav-employer-applications-link");
            if (item) return item;

            const divider = userMenu.querySelector(".dropdown-divider");
            item = document.createElement("li");
            item.id = "nav-employer-applications-link";
            item.className = "d-none";
            item.innerHTML = '<a class="dropdown-item" href="/Home/EmployerApplications"><i class="fas fa-file-signature me-2"></i>Hồ sơ ứng tuyển</a>';

            if (divider && divider.parentElement) {
                divider.parentElement.insertBefore(item, divider);
            } else {
                userMenu.appendChild(item);
            }

            return item;
        }

        function render(account) {
            const loginItem = document.getElementById("nav-auth-login-item");
            const userItem = document.getElementById("nav-auth-user-item");
            const userName = document.getElementById("nav-auth-user-name");
            const userRole = document.getElementById("nav-auth-user-role");
            const userIcon = document.getElementById("nav-auth-user-icon");
            const userAvatar = document.getElementById("nav-auth-user-avatar");
            const adminLink = document.getElementById("nav-admin-link");
            const manageLink = document.getElementById("nav-manage-link");
            const hostAppointmentsLink = ensureHostAppointmentsItem();
            const employerApplicationsLink = ensureEmployerApplicationsItem();
            const postRoomLink = document.getElementById("nav-post-room-link");
            const postJobLink = document.getElementById("nav-post-job-link");
            const notificationItems = document.querySelectorAll(".nav-notification-wrapper");

            if (!loginItem || !userItem || !userName || !userRole) return;

            if (manageLink) manageLink.classList.add("d-none");
            if (hostAppointmentsLink) hostAppointmentsLink.classList.add("d-none");
            if (employerApplicationsLink) employerApplicationsLink.classList.add("d-none");
            if (adminLink) adminLink.classList.add("d-none");
            if (postRoomLink) postRoomLink.classList.add("d-none");
            if (postJobLink) postJobLink.classList.add("d-none");
            notificationItems.forEach(item => item.classList.add("d-none"));

            if (!account || !account.email) {
                loginItem.classList.remove("d-none");
                userItem.classList.add("d-none");
                if (resetNotifications) resetNotifications();
                if (stopNotifications) stopNotifications();
                return;
            }

            loginItem.classList.add("d-none");
            userItem.classList.remove("d-none");
            notificationItems.forEach(item => item.classList.remove("d-none"));

            userName.textContent = account.fullName || account.email.split('@')[0];
            userRole.innerHTML = `<i class="fas fa-shield-alt me-1"></i> ${roleToDisplayName(account.role)}`;

            if (account.avatarUrl && userAvatar && userIcon) {
                userAvatar.src = account.avatarUrl;
                userAvatar.classList.remove("d-none");
                userIcon.classList.add("d-none");
            } else if (userAvatar && userIcon) {
                userAvatar.classList.add("d-none");
                userIcon.classList.remove("d-none");
            }

            var role = (account.role || "").toLowerCase();
            if (role === "admin" && adminLink) {
                adminLink.classList.remove("d-none");
            }
            if (role === "host" || role === "employer" || role === "student") {
                if (manageLink) {
                    manageLink.classList.remove("d-none");
                    if (role === "student") {
                        manageLink.querySelector('a').href = '/Home/ManageRoommates';
                    } else {
                        manageLink.querySelector('a').href = '/Home/Manage';
                    }
                }
            }
            if (role === "host" && hostAppointmentsLink) hostAppointmentsLink.classList.remove("d-none");
            if (role === "employer" && employerApplicationsLink) employerApplicationsLink.classList.remove("d-none");
            if (role === "host" && postRoomLink) postRoomLink.classList.remove("d-none");
            if (role === "employer" && postJobLink) postJobLink.classList.remove("d-none");
        }

        function wireLogout() {
            const logoutButton = document.getElementById("nav-auth-logout-btn");
            if (!logoutButton) return;

            logoutButton.addEventListener("click", function () {
                clearStoredAuth();
                render(null);
                if (resetNotifications) resetNotifications();
                if (stopNotifications) stopNotifications();
                window.location.href = "/Home/Auth";
            });
        }

        return { render, wireLogout };
    }

    window.UniMap360NavbarAuth = { createNavbarAuth };
})();

