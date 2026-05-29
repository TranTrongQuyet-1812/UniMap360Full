(function () {
    function roleToDisplayName(role) {
        switch ((role || "").toLowerCase()) {
            case "admin":
                return "Quản trị viên";
            case "student":
                return "Sinh viên";
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
            return document.getElementById("nav-host-appointments-link");
        }

        function ensureEmployerApplicationsItem() {
            return document.getElementById("nav-employer-applications-link");
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
            const roommatesLink = document.getElementById("nav-roommates-link");
            const notificationItems = document.querySelectorAll(".nav-notification-wrapper");

            if (!loginItem || !userItem || !userName || !userRole) return;

            if (manageLink) manageLink.classList.add("d-none");
            if (hostAppointmentsLink) hostAppointmentsLink.classList.add("d-none");
            if (employerApplicationsLink) employerApplicationsLink.classList.add("d-none");
            if (adminLink) adminLink.classList.add("d-none");
            if (postRoomLink) postRoomLink.classList.add("d-none");
            if (postJobLink) postJobLink.classList.add("d-none");
            if (roommatesLink) roommatesLink.classList.add("d-none");
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
            if (role === "student" && roommatesLink) {
                roommatesLink.classList.remove("d-none");
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
                const promise = clearStoredAuth();
                render(null);
                if (resetNotifications) resetNotifications();
                if (stopNotifications) stopNotifications();

                const redirect = () => {
                    window.location.href = "/Home/Auth";
                };

                if (promise instanceof Promise) {
                    promise.finally(redirect);
                } else {
                    redirect();
                }
            });
        }

        return { render, wireLogout };
    }

    window.UniMap360NavbarAuth = { createNavbarAuth };
})();

