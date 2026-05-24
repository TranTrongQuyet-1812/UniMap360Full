document.addEventListener('DOMContentLoaded', function() {
            // Lấy ID và loại (room/job) từ URL (?id=...&type=...)
            const urlParams = new URLSearchParams(window.location.search);
            const id = urlParams.get('id');
            const type = urlParams.get('type') || 'room';
            let detailMiniMap = null;

            function initMiniMap(lat, lng, isRoom) {
                const mapEl = document.getElementById('detail-mini-map');
                const emptyEl = document.getElementById('detail-mini-map-empty');
                if (!mapEl || !emptyEl) return;

                if (!Number.isFinite(lat) || !Number.isFinite(lng) || typeof L === 'undefined') {
                    mapEl.classList.add('d-none');
                    emptyEl.classList.remove('d-none');
                    return;
                }

                mapEl.classList.remove('d-none');
                emptyEl.classList.add('d-none');

                if (detailMiniMap) {
                    detailMiniMap.remove();
                    detailMiniMap = null;
                }

                var southwest = L.latLng(4.0, 99.0);
                var northeast = L.latLng(24.5, 120.0);
                var vietnamBounds = L.latLngBounds(southwest, northeast);

                detailMiniMap = L.map('detail-mini-map', {
                    zoomControl: false,
                    attributionControl: false,
                    dragging: false,
                    touchZoom: false,
                    doubleClickZoom: false,
                    scrollWheelZoom: false,
                    boxZoom: false,
                    keyboard: false,
                    tap: false,
                    minZoom: 6,
                    maxBounds: vietnamBounds,
                    maxBoundsViscosity: 0.8
                }).setView([lat, lng], 15);

                L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
                    maxZoom: 19
                }).addTo(detailMiniMap);

                const pinColor = isRoom ? '#780115' : '#1d4ed8';
                L.circleMarker([lat, lng], {
                    radius: 8,
                    color: '#ffffff',
                    weight: 2,
                    fillColor: pinColor,
                    fillOpacity: 1
                }).addTo(detailMiniMap);

                setTimeout(function() {
                    if (detailMiniMap) detailMiniMap.invalidateSize();
                }, 150);
            }

            function renderStars(value) {
                const safe = Math.max(0, Math.min(5, Math.round(value || 0)));
                return '★'.repeat(safe) + '☆'.repeat(5 - safe);
            }

            function getAccessToken() {
                if (window.UniMap360AuthStore && typeof window.UniMap360AuthStore.getStoredToken === 'function') {
                    return window.UniMap360AuthStore.getStoredToken();
                }
                if (typeof window.getToken === 'function') return window.getToken();
                return window.UniMap360Core?.getToken?.() || null;
            }

            function getCurrentAccount() {
                return window.UniMap360AuthStore?.getStoredAccount?.() || null;
            }

            function isStudentAccount(account) {
                return String(account?.role || '').toLowerCase() === 'student';
            }

            function renderReviewList(items) {
                const listEl = document.getElementById('detail-review-list');
                if (!Array.isArray(items) || items.length === 0) {
                    listEl.innerHTML = '<div class="text-muted">Chưa có đánh giá nào. Hãy là người đầu tiên chia sẻ trải nghiệm.</div>';
                    return;
                }

                listEl.innerHTML = items.map(function(r) {
                    const stars = renderStars(r.rating || 0);
                    const reviewer = window.escapeHtml(r.reviewerName || 'Sinh viên');
                    const time = window.escapeHtml(r.createdAt ? new Date(r.createdAt).toLocaleDateString('vi-VN') : '---');
                    const comment = window.escapeHtml(r.comment && r.comment.trim() ? r.comment : 'Không có bình luận.');
                    return `
                        <div class="mb-2 p-2" style="border-bottom:1px solid #efefef;">
                            <div class="d-flex justify-content-between align-items-center mb-1">
                                <strong>${reviewer}</strong>
                                <span class="small text-muted">${time}</span>
                            </div>
                            <div style="color:#f2b01e;">${stars}</div>
                            <div class="text-muted">${comment}</div>
                        </div>
                    `;
                }).join('');
            }

            function loadReviews(targetType, targetId) {
                fetch(`/api/reviews/${targetType}/${targetId}?page=1&pageSize=5`)
                    .then(function(res) {
                        if (!res.ok) throw new Error('Không tải được đánh giá.');
                        return res.json();
                    })
                    .then(function(json) {
                        var data = (json && json.success === true && json.data !== undefined) ? json.data : json;
                        const avg = Number(data.avgRating || 0);
                        const total = Number(data.totalReviews || 0);
                        document.getElementById('detail-review-stars').textContent = renderStars(avg);
                        document.getElementById('detail-review-avg').textContent = avg.toFixed(1);
                        document.getElementById('detail-review-count').textContent = `(${total} đánh giá)`;
                        renderReviewList(data.items || []);
                    })
                    .catch(function() {
                        document.getElementById('detail-review-list').innerHTML = '<div class="text-danger">Không tải được dữ liệu đánh giá.</div>';
                    });
            }

            function wireReviewForm(targetType, targetId) {
                const token = getAccessToken();
                const formWrap = document.getElementById('detail-review-form-wrap');
                const note = document.getElementById('detail-review-form-note');
                const submitBtn = document.getElementById('detail-review-submit');

                if (!token || !isStudentAccount(getCurrentAccount())) {
                    submitBtn.disabled = true;
                    note.textContent = 'Bạn cần đăng nhập bằng tài khoản Student để gửi đánh giá.';
                    formWrap.classList.add('opacity-75');
                    return;
                }

                submitBtn.addEventListener('click', function() {
                    const rating = Number(document.getElementById('detail-review-rating').value || 5);
                    const comment = document.getElementById('detail-review-comment').value || '';

                    submitBtn.disabled = true;
                    note.textContent = 'Đang gửi đánh giá...';

                    fetch('/api/reviews', {
                        method: 'POST',
                        headers: {
                            'Content-Type': 'application/json'
                        },
                        body: JSON.stringify({
                            targetType: targetType,
                            targetId: Number(targetId),
                            rating: rating,
                            comment: comment
                        })
                    })
                    .then(function(res) {
                        if (!res.ok) throw new Error('Gửi đánh giá thất bại.');
                        return res.json();
                    })
                    .then(function() {
                        note.textContent = 'Gửi đánh giá thành công.';
                        loadReviews(targetType, targetId);
                    })
                    .catch(function() {
                        note.textContent = 'Không gửi được đánh giá. Vui lòng thử lại.';
                    })
                    .finally(function() {
                        submitBtn.disabled = false;
                    });
                });
            }

            function resetContactButton(contactBtn) {
                contactBtn.disabled = false;
                contactBtn.classList.remove('opacity-75');
                contactBtn.onclick = null;
                contactBtn.innerHTML = '<i class="fas fa-paper-plane me-2"></i> Liên hệ ngay';
                contactBtn.removeAttribute('title');
            }

            function setContactDisabled(contactBtn, noteEl, reason) {
                contactBtn.disabled = true;
                contactBtn.classList.add('opacity-75');
                contactBtn.title = reason;
                noteEl.textContent = reason;
            }

            function resetContactButtonSafe(contactBtn) {
                const freshBtn = contactBtn.cloneNode(true);
                contactBtn.parentNode.replaceChild(freshBtn, contactBtn);
                resetContactButton(freshBtn);
                return freshBtn;
            }
            function configureContactAction(item, isRoom, itemId) {
                let contactBtn = document.getElementById('detail-contact-btn');
                const noteEl = document.getElementById('detail-contact-note');
                const appointmentModal = document.getElementById('detail-appointment-modal');
                const appointmentBackdrop = document.getElementById('detail-appointment-backdrop');
                const appointmentCloseEl = document.getElementById('detail-appointment-close');
                const appointmentTimeEl = document.getElementById('detail-appointment-time');
                const appointmentPhoneEl = document.getElementById('detail-appointment-phone');
                const appointmentNoteEl = document.getElementById('detail-appointment-note');
                const appointmentSubmitEl = document.getElementById('detail-appointment-submit');
                const appointmentCancelEl = document.getElementById('detail-appointment-cancel');
                const appointmentFeedbackEl = document.getElementById('detail-appointment-feedback');
                const historyWrapEl = document.getElementById('detail-appointment-history-wrap');
                const historyEmptyEl = document.getElementById('detail-appointment-history-empty');
                const historyListEl = document.getElementById('detail-appointment-history-list');
                const jobApplyModal = document.getElementById('detail-job-apply-modal');
                const jobApplyBackdrop = document.getElementById('detail-job-apply-backdrop');
                const jobApplyCloseEl = document.getElementById('detail-job-apply-close');
                const jobApplyEmailEl = document.getElementById('detail-job-apply-email');
                const jobApplyPhoneEl = document.getElementById('detail-job-apply-phone');
                const jobApplyCvUrlEl = document.getElementById('detail-job-apply-cv-url');
                const jobApplyCvFileEl = document.getElementById('detail-job-apply-cv-file');
                const jobApplySubmitEl = document.getElementById('detail-job-apply-submit');
                const jobApplyCancelEl = document.getElementById('detail-job-apply-cancel');
                const jobApplyFeedbackEl = document.getElementById('detail-job-apply-feedback');
                let appointmentSuccessTimer = null;

                contactBtn = resetContactButtonSafe(contactBtn);
                noteEl.textContent = '';
                appointmentFeedbackEl.textContent = '';
                appointmentModal.classList.add('d-none');
                appointmentTimeEl.value = '';
                appointmentPhoneEl.value = item.contactPhone || '';
                appointmentNoteEl.value = '';
                if (jobApplyModal) jobApplyModal.classList.add('d-none');
                if (jobApplyEmailEl) jobApplyEmailEl.value = '';
                if (jobApplyPhoneEl) jobApplyPhoneEl.value = '';
                if (jobApplyCvUrlEl) jobApplyCvUrlEl.value = '';
                if (jobApplyCvFileEl) jobApplyCvFileEl.value = '';
                if (jobApplyFeedbackEl) jobApplyFeedbackEl.textContent = '';

                const token = getAccessToken();
                const isStudent = isStudentAccount(getCurrentAccount());
                const appointmentCooldownMs = 45000;
                let hasPendingAppointment = false;
                let lastAppointmentSentAt = 0;

                const getCooldownRemainingMs = function() {
                    if (!lastAppointmentSentAt) return 0;
                    const elapsed = Date.now() - lastAppointmentSentAt;
                    return Math.max(0, appointmentCooldownMs - elapsed);
                };

                const formatCooldownText = function(remainingMs) {
                    const seconds = Math.ceil(remainingMs / 1000);
                    return `${seconds} giây`;
                };

                const updateStudentContactButton = function() {
                    if (!hasPendingAppointment) {
                        contactBtn.innerHTML = '<i class="fas fa-calendar-check me-2"></i> Đặt lịch xem phòng';
                        return;
                    }

                    contactBtn.innerHTML = '<i class="fas fa-rotate-right me-2"></i> Gửi lại yêu cầu';
                };

                const normalizeStatus = function(status) {
                    return (status || '').toString().trim().toLowerCase();
                };

                const getStatusMeta = function(status) {
                    const normalized = normalizeStatus(status);
                    if (normalized === 'confirmed') return { text: 'Đã xác nhận', className: 'bg-success-subtle text-success' };
                    if (normalized === 'rejected') return { text: 'Đã từ chối', className: 'bg-danger-subtle text-danger' };
                    if (normalized === 'rescheduled') return { text: 'Đề xuất giờ mới', className: 'bg-warning-subtle text-warning-emphasis' };
                    return { text: 'Đang chờ', className: 'bg-secondary-subtle text-secondary-emphasis' };
                };

                const formatDateTime = function(value) {
                    if (!value) return 'Chưa xác định';
                    const date = new Date(value);
                    if (Number.isNaN(date.getTime())) return 'Chưa xác định';
                    return date.toLocaleString('vi-VN');
                };

                const renderAppointmentHistory = function(items) {
                    if (!historyWrapEl || !historyEmptyEl || !historyListEl) return;

                    historyListEl.innerHTML = '';
                    if (!Array.isArray(items) || items.length === 0) {
                        historyEmptyEl.classList.remove('d-none');
                        return;
                    }

                    historyEmptyEl.classList.add('d-none');
                    const html = items.slice(0, 5).map(function(entry) {
                        const statusMeta = getStatusMeta(entry.status);
                        const noteValue = window.escapeHtml((entry.studentNote || '').toString().trim());
                        const phoneValue = window.escapeHtml((entry.contactPhone || '').toString().trim());
                        const scheduledTime = formatDateTime(entry.scheduledAt);
                        const createdTime = formatDateTime(entry.createdAt);

                        return `
                            <div class="p-3 rounded-4 mb-2 shadow-sm border" style="background: linear-gradient(145deg, #ffffff, #fcfcfc); border-color: rgba(0,0,0,0.05) !important;">
                                <div class="d-flex justify-content-between align-items-start mb-2">
                                    <div>
                                        <div class="small text-muted fw-600 mb-1" style="font-size: 0.75rem; text-transform: uppercase; letter-spacing: 0.5px;">Thời gian hẹn xem</div>
                                        <div class="fw-800 text-dark d-flex align-items-center">
                                            <i class="far fa-calendar-check text-primary me-2"></i>
                                            ${scheduledTime}
                                        </div>
                                    </div>
                                    <span class="badge rounded-pill px-3 py-2 ${statusMeta.className}" style="font-size: 0.7rem; font-weight: 800; letter-spacing: 0.3px;">
                                        ${statusMeta.text.toUpperCase()}
                                    </span>
                                </div>
                                
                                <div class="mt-3 pt-2 border-top" style="border-style: dashed !important; border-color: rgba(0,0,0,0.08) !important;">
                                    <div class="d-flex flex-column gap-1">
                                        <div class="small text-muted d-flex align-items-center">
                                            <i class="fas fa-paper-plane me-2 opacity-50" style="width: 14px;"></i>
                                            Gửi lúc: ${createdTime}
                                        </div>
                                        ${phoneValue ? `
                                        <div class="small text-muted d-flex align-items-center">
                                            <i class="fas fa-phone-alt me-2 opacity-50" style="width: 14px;"></i>
                                            Liên hệ: <span class="ms-1 fw-bold text-dark">${phoneValue}</span>
                                        </div>` : ''}
                                        ${noteValue ? `
                                        <div class="small text-dark mt-1 p-2 bg-light rounded-3" style="font-style: italic;">
                                            <i class="fas fa-comment-dots me-2 text-primary opacity-50"></i>
                                            "${noteValue}"
                                        </div>` : ''}
                                    </div>
                                </div>
                            </div>
                        `;
                    }).join('');

                    historyListEl.innerHTML = html;
                };

                const loadStudentRoomHistory = function() {
                    if (!isRoom || !isStudent || !token) return Promise.resolve();
                    if (!historyWrapEl) return Promise.resolve();

                    historyWrapEl.classList.remove('d-none');
                    historyEmptyEl.classList.remove('d-none');
                    historyEmptyEl.textContent = 'Đang tải lịch sử yêu cầu...';
                    historyListEl.innerHTML = '';

                    return fetch('/api/room-appointments/my?limit=100', {
                        credentials: 'same-origin'
                    })
                    .then(function(res) {
                        if (!res.ok) throw new Error('Không tải được lịch sử yêu cầu.');
                        return res.json();
                    })
                    .then(function(json) {
                        var data = (json && json.success === true && json.data !== undefined) ? json.data : json;
                        const items = Array.isArray(data?.items) ? data.items : [];
                        const roomIdNumber = Number(itemId);
                        const roomItems = items
                            .filter(function(x) { return Number(x.roomId) === roomIdNumber; })
                            .sort(function(a, b) { return new Date(b.createdAt) - new Date(a.createdAt); });

                        hasPendingAppointment = roomItems.some(function(x) {
                            return normalizeStatus(x.status) === 'pending';
                        });

                        updateStudentContactButton();
                        historyEmptyEl.textContent = 'Chưa có yêu cầu nào cho phòng này.';
                        renderAppointmentHistory(roomItems);
                    })
                    .catch(function() {
                        historyEmptyEl.classList.remove('d-none');
                        historyEmptyEl.textContent = 'Không tải được lịch sử yêu cầu.';
                    });
                };

                const openAppointmentModal = function() {
                    noteEl.textContent = '';
                    appointmentFeedbackEl.classList.remove('text-success', 'text-danger', 'fw-semibold', 'detail-success-bounce');
                    appointmentFeedbackEl.classList.add('text-muted');
                    appointmentFeedbackEl.textContent = '';
                    appointmentSubmitEl.innerHTML = hasPendingAppointment ? 'Gửi lại yêu cầu' : 'Gửi yêu cầu';
                    appointmentModal.classList.remove('d-none');
                };
                const closeAppointmentModal = function() {
                    if (appointmentSuccessTimer) {
                        clearTimeout(appointmentSuccessTimer);
                        appointmentSuccessTimer = null;
                    }
                    appointmentModal.classList.add('d-none');
                    appointmentSubmitEl.classList.remove('detail-success-pulse');
                    appointmentSubmitEl.disabled = false;
                    appointmentFeedbackEl.classList.remove('text-success', 'text-danger', 'fw-semibold', 'detail-success-bounce');
                    appointmentFeedbackEl.classList.add('text-muted');
                    appointmentFeedbackEl.textContent = '';
                };

                const showAppointmentSuccess = function(message) {
                    appointmentFeedbackEl.classList.remove('text-muted', 'text-danger');
                    appointmentFeedbackEl.classList.add('text-success', 'fw-semibold', 'detail-success-bounce');
                    appointmentFeedbackEl.innerHTML = `<i class="fas fa-circle-check me-1"></i>${message}`;

                    appointmentSubmitEl.classList.add('detail-success-pulse');
                    hasPendingAppointment = true;
                    lastAppointmentSentAt = Date.now();
                    updateStudentContactButton();
                    loadStudentRoomHistory();

                    appointmentSuccessTimer = setTimeout(function() {
                        noteEl.textContent = message;
                        closeAppointmentModal();
                    }, 900);
                };

                appointmentBackdrop.onclick = closeAppointmentModal;
                appointmentCloseEl.onclick = closeAppointmentModal;
                appointmentCancelEl.onclick = closeAppointmentModal;

                if (!isRoom) {
                    if (historyWrapEl) historyWrapEl.classList.add('d-none');

                    if (isStudent && token) {
                        let hasPendingJobApplication = false;
                        let isApplyingJob = false;
                        let appliedContactEmail = '';
                        let appliedContactPhone = '';
                        let appliedCvUrl = '';

                        const setApplyButtonState = function() {
                            if (hasPendingJobApplication) {
                                contactBtn.disabled = true;
                                contactBtn.classList.add('opacity-75');
                                contactBtn.innerHTML = '<i class="fas fa-hourglass-half me-2"></i> Đã ứng tuyển (đang chờ)';
                                noteEl.textContent = 'Bạn đã có hồ sơ đang chờ cho công việc này.';
                                return;
                            }

                            contactBtn.disabled = false;
                            contactBtn.classList.remove('opacity-75');
                            contactBtn.innerHTML = '<i class="fas fa-paper-plane me-2"></i> Ứng tuyển nhanh';
                            noteEl.textContent = 'Gửi hồ sơ ngay trên hệ thống để nhà tuyển dụng phản hồi.';
                        };

                        const closeJobApplyModal = function() {
                            if (jobApplyModal) jobApplyModal.classList.add('d-none');
                            if (jobApplySubmitEl) {
                                jobApplySubmitEl.disabled = false;
                                jobApplySubmitEl.classList.remove('detail-success-pulse');
                                jobApplySubmitEl.innerHTML = 'Gửi ứng tuyển';
                            }
                            if (jobApplyFeedbackEl) {
                                jobApplyFeedbackEl.classList.remove('text-danger', 'text-success', 'fw-semibold', 'detail-success-bounce');
                                jobApplyFeedbackEl.classList.add('text-muted');
                                jobApplyFeedbackEl.textContent = '';
                            }
                        };

                        const openJobApplyModal = function() {
                            if (!jobApplyModal) return;
                            if (jobApplyEmailEl && !jobApplyEmailEl.value.trim() && appliedContactEmail) {
                                jobApplyEmailEl.value = appliedContactEmail;
                            }
                            if (jobApplyPhoneEl && !jobApplyPhoneEl.value.trim() && appliedContactPhone) {
                                jobApplyPhoneEl.value = appliedContactPhone;
                            }
                            if (jobApplyCvUrlEl && !jobApplyCvUrlEl.value.trim() && appliedCvUrl) {
                                jobApplyCvUrlEl.value = appliedCvUrl;
                            }
                            if (jobApplyCvFileEl) jobApplyCvFileEl.value = '';
                            if (jobApplyFeedbackEl) {
                                jobApplyFeedbackEl.classList.remove('text-danger', 'text-success', 'fw-semibold', 'detail-success-bounce');
                                jobApplyFeedbackEl.classList.add('text-muted');
                                jobApplyFeedbackEl.textContent = '';
                            }
                            jobApplyModal.classList.remove('d-none');
                        };

                        if (jobApplyBackdrop) jobApplyBackdrop.onclick = closeJobApplyModal;
                        if (jobApplyCloseEl) jobApplyCloseEl.onclick = closeJobApplyModal;
                        if (jobApplyCancelEl) jobApplyCancelEl.onclick = closeJobApplyModal;

                        const loadMyJobApplications = function() {
                            return fetch('/api/job-applications/my?limit=200', {
                                credentials: 'same-origin'
                            })
                            .then(function(res) {
                                if (!res.ok) return null;
                                return res.json();
                            })
                            .then(function(json) {
                                var data = (json && json.success === true && json.data !== undefined) ? json.data : json;
                                const items = Array.isArray(data?.items) ? data.items : [];
                                const jobIdNumber = Number(itemId);
                                const pendingItem = items.find(function(x) {
                                    return Number(x.jobId) === jobIdNumber && normalizeStatus(x.status) === 'pending';
                                });
                                hasPendingJobApplication = !!pendingItem;
                                if (pendingItem) {
                                    appliedContactEmail = pendingItem.contactEmail || '';
                                    appliedContactPhone = pendingItem.contactPhone || '';
                                    appliedCvUrl = pendingItem.cvUrl || '';
                                }
                                setApplyButtonState();
                            })
                            .catch(function() {
                                setApplyButtonState();
                            });
                        };

                        setApplyButtonState();
                        loadMyJobApplications();

                        contactBtn.onclick = function() {
                            if (isApplyingJob || hasPendingJobApplication) return;
                            openJobApplyModal();
                        };

                        if (jobApplySubmitEl) {
                            jobApplySubmitEl.onclick = function() {
                                if (isApplyingJob || hasPendingJobApplication) return;

                                const contactEmail = (jobApplyEmailEl?.value || '').trim();
                                const contactPhone = (jobApplyPhoneEl?.value || '').trim();
                                const cvUrl = (jobApplyCvUrlEl?.value || '').trim();
                                const cvFile = jobApplyCvFileEl?.files?.[0] || null;

                                if (!contactEmail) {
                                    if (jobApplyFeedbackEl) jobApplyFeedbackEl.textContent = 'Vui lòng nhập email liên hệ.';
                                    return;
                                }
                                if (!contactPhone) {
                                    if (jobApplyFeedbackEl) jobApplyFeedbackEl.textContent = 'Vui lòng nhập số điện thoại liên hệ.';
                                    return;
                                }
                                if (!cvUrl && !cvFile) {
                                    if (jobApplyFeedbackEl) jobApplyFeedbackEl.textContent = 'Vui lòng nhập link CV hoặc chọn file CV.';
                                    return;
                                }

                                isApplyingJob = true;
                                jobApplySubmitEl.disabled = true;
                                jobApplySubmitEl.innerHTML = '<i class="fas fa-spinner fa-spin me-2"></i> Đang gửi...';
                                if (jobApplyFeedbackEl) {
                                    jobApplyFeedbackEl.classList.remove('text-danger', 'text-success', 'fw-semibold', 'detail-success-bounce');
                                    jobApplyFeedbackEl.classList.add('text-muted');
                                    jobApplyFeedbackEl.textContent = 'Đang gửi ứng tuyển...';
                                }

                                const formData = new FormData();
                                formData.append('jobId', String(Number(itemId)));
                                formData.append('contactEmail', contactEmail);
                                formData.append('contactPhone', contactPhone);
                                if (cvUrl) formData.append('cvUrl', cvUrl);
                                if (cvFile) formData.append('cvFile', cvFile);

                                fetch('/api/job-applications', {
                                    method: 'POST',
                                    credentials: 'same-origin',
                                    body: formData
                                })
                                .then(function(res) {
                                    return res.json().then(function(json) {
                                        var data = (json && json.success === true && json.data !== undefined) ? json.data : json;
                                        if (!res.ok) {
                                            throw new Error(data?.message || json?.error?.message || 'Gửi ứng tuyển thất bại.');
                                        }
                                        return data;
                                    });
                                })
                                .then(function(data) {
                                    hasPendingJobApplication = true;
                                    appliedContactEmail = contactEmail;
                                    appliedContactPhone = contactPhone;
                                    appliedCvUrl = cvUrl || appliedCvUrl;

                                    if (jobApplyFeedbackEl) {
                                        jobApplyFeedbackEl.classList.remove('text-muted', 'text-danger');
                                        jobApplyFeedbackEl.classList.add('text-success', 'fw-semibold', 'detail-success-bounce');
                                        jobApplyFeedbackEl.innerHTML = `<i class="fas fa-circle-check me-1"></i>${data?.message || 'Ứng tuyển thành công.'}`;
                                    }
                                    jobApplySubmitEl.classList.add('detail-success-pulse');
                                    contactBtn.classList.add('detail-success-pulse');
                                    noteEl.textContent = data?.message || 'Ứng tuyển thành công.';
                                    setApplyButtonState();
                                    loadMyJobApplications();

                                    setTimeout(function() {
                                        closeJobApplyModal();
                                    }, 900);
                                })
                                .catch(function(err) {
                                    if (jobApplyFeedbackEl) {
                                        jobApplyFeedbackEl.classList.remove('text-muted', 'text-success', 'fw-semibold', 'detail-success-bounce');
                                        jobApplyFeedbackEl.classList.add('text-danger');
                                        jobApplyFeedbackEl.textContent = err?.message || 'Không gửi được ứng tuyển.';
                                    }
                                    jobApplySubmitEl.disabled = false;
                                    jobApplySubmitEl.innerHTML = 'Gửi ứng tuyển';
                                })
                                .finally(function() {
                                    isApplyingJob = false;
                                });
                            };
                        }

                        return;
                    }

                    if (!token) {
                        contactBtn.innerHTML = '<i class="fas fa-user-lock me-2"></i> Đăng nhập để ứng tuyển';
                        contactBtn.onclick = function() {
                            window.location.href = '/Home/Auth';
                        };
                        noteEl.textContent = 'Đăng nhập tài khoản Student để gửi hồ sơ nhanh.';
                        return;
                    }

                    if (item.contactPhone) {
                        contactBtn.innerHTML = '<i class="fas fa-phone-alt me-2"></i> Liên hệ qua điện thoại';
                        contactBtn.onclick = function() {
                            window.location.href = `tel:${item.contactPhone}`;
                        };
                        noteEl.textContent = 'Bạn có thể gọi trực tiếp để trao đổi nhanh.';
                        return;
                    }

                    setContactDisabled(contactBtn, noteEl, 'Tin đăng chưa có số điện thoại liên hệ.');
                    return;
                }

                if (isStudent) {
                    if (historyWrapEl) historyWrapEl.classList.remove('d-none');
                    loadStudentRoomHistory();
                    updateStudentContactButton();

                    contactBtn.onclick = function() {
                        const remainingMs = getCooldownRemainingMs();
                        if (remainingMs > 0) {
                            noteEl.textContent = `Bạn vừa gửi yêu cầu. Vui lòng chờ ${formatCooldownText(remainingMs)} trước khi gửi lại.`;
                            return;
                        }

                        if (hasPendingAppointment) {
                            const wantsResend = window.confirm('Bạn đang có yêu cầu đang chờ phản hồi. Bạn vẫn muốn gửi yêu cầu mới?');
                            if (!wantsResend) return;
                        }

                        openAppointmentModal();
                    };

                    appointmentSubmitEl.onclick = function() {
                        const remainingMs = getCooldownRemainingMs();
                        if (remainingMs > 0) {
                            appointmentFeedbackEl.classList.remove('text-success', 'text-danger', 'fw-semibold', 'detail-success-bounce');
                            appointmentFeedbackEl.classList.add('text-muted');
                            appointmentFeedbackEl.textContent = `Vui lòng chờ ${formatCooldownText(remainingMs)} trước khi gửi lại.`;
                            return;
                        }

                        const scheduledAtRaw = appointmentTimeEl.value;
                        const phone = appointmentPhoneEl.value.trim();
                        const note = appointmentNoteEl.value.trim();

                        if (!scheduledAtRaw) {
                            appointmentFeedbackEl.textContent = 'Vui lòng chọn thời gian hẹn.';
                            return;
                        }

                        const scheduledDate = new Date(scheduledAtRaw);
                        if (Number.isNaN(scheduledDate.getTime())) {
                            appointmentFeedbackEl.textContent = 'Thời gian hẹn không hợp lệ.';
                            return;
                        }

                        appointmentSubmitEl.disabled = true;
                        appointmentFeedbackEl.classList.remove('text-success', 'text-danger', 'fw-semibold', 'detail-success-bounce');
                        appointmentFeedbackEl.classList.add('text-muted');
                        appointmentFeedbackEl.textContent = 'Đang gửi yêu cầu...';

                        fetch('/api/room-appointments', {
                            method: 'POST',
                            headers: {
                                'Content-Type': 'application/json'
                            },
                            body: JSON.stringify({
                                roomId: Number(itemId),
                                scheduledAt: scheduledDate.toISOString(),
                                contactPhone: phone || null,
                                note: note || null
                            })
                        })
                        .then(function(res) {
                            return res.json().then(function(json) {
                                var data = (json && json.success === true && json.data !== undefined) ? json.data : json;
                                if (!res.ok) {
                                    throw new Error(data?.message || json?.error?.message || 'Gửi yêu cầu thất bại.');
                                }
                                return data;
                            });
                        })
                        .then(function(data) {
                            const successMessage = data?.message || 'Đặt lịch thành công.';
                            showAppointmentSuccess(successMessage);
                        })
                        .catch(function(err) {
                            appointmentFeedbackEl.classList.remove('text-muted', 'text-success', 'fw-semibold', 'detail-success-bounce');
                            appointmentFeedbackEl.classList.add('text-danger');
                            appointmentFeedbackEl.textContent = err?.message || 'Không gửi được yêu cầu.';
                            appointmentSubmitEl.disabled = false;
                        })
                        ;
                    };

                    return;
                }

                if (historyWrapEl) historyWrapEl.classList.add('d-none');

                // Trường hợp chưa đăng nhập: Chuyển hướng tới Auth
                if (!token) {
                    contactBtn.innerHTML = '<i class="fas fa-user-lock me-2"></i> Đăng nhập để đặt lịch';
                    contactBtn.onclick = function() {
                        window.location.href = '/Home/Auth';
                    };
                    if (noteEl) noteEl.textContent = 'Đăng nhập tài khoản Student để đặt lịch xem phòng trực tuyến.';
                    return;
                }

                if (item.contactPhone) {
                    contactBtn.innerHTML = '<i class="fas fa-phone-alt me-2"></i> Liên hệ qua điện thoại';
                    contactBtn.onclick = function() {
                        window.location.href = `tel:${item.contactPhone}`;
                    };
                    if (noteEl) noteEl.textContent = 'Bạn có thể gọi trực tiếp để trao đổi nhanh.';
                    return;
                }

                setContactDisabled(contactBtn, noteEl, 'Tin đăng chưa có số điện thoại. Đăng nhập Student để đặt lịch xem phòng.');
            }
            function configureChatAction(item, isRoom) {
                const chatBtn = document.getElementById('detail-chat-btn');
                const chatNoteEl = document.getElementById('detail-chat-note');
                if (!chatBtn || !chatNoteEl) return;

                const token = getAccessToken();
                const account = window.UniMap360AuthStore?.getStoredAccount?.() || null;
                const currentAccountId = Number(account?.accountId || 0);
                const ownerAccountId = Number(item?.ownerAccountId || 0);
                const ownerName = (item?.ownerDisplayName || '').toString().trim();
                const ownerRoleText = isRoom ? 'chủ trọ' : 'nhà tuyển dụng';

                chatBtn.disabled = false;
                chatBtn.classList.remove('opacity-75');

                if (!token || !currentAccountId) {
                    chatBtn.innerHTML = '<i class="fas fa-user-lock me-2"></i> Đăng nhập để nhắn tin';
                    chatBtn.onclick = function() {
                        window.location.href = '/Home/Auth';
                    };
                    chatNoteEl.textContent = `Đăng nhập để chat với ${ownerRoleText}.`;
                    return;
                }

                if (!ownerAccountId) {
                    chatBtn.innerHTML = '<i class="fas fa-comments me-2"></i> Nhắn tin';
                    chatBtn.disabled = true;
                    chatBtn.classList.add('opacity-75');
                    chatNoteEl.textContent = 'Chưa xác định được tài khoản đăng tin để nhắn.';
                    return;
                }

                if (ownerAccountId === currentAccountId) {
                    chatBtn.innerHTML = '<i class="fas fa-circle-info me-2"></i> Bài đăng của bạn';
                    chatBtn.disabled = true;
                    chatBtn.classList.add('opacity-75');
                    chatNoteEl.textContent = 'Đây là bài đăng của bạn.';
                    return;
                }

                chatBtn.innerHTML = '<i class="fas fa-comments me-2"></i> Nhắn tin ngay';
                chatNoteEl.textContent = ownerName
                    ? `Trao đổi trực tiếp với ${ownerName}.`
                    : `Trao đổi trực tiếp với ${ownerRoleText}.`;

                chatBtn.onclick = function() {
                    const chatApi = window.UniMap360ChatWidget;
                    if (!chatApi || typeof chatApi.openDirectChat !== 'function') {
                        chatNoteEl.textContent = 'Không khởi động được hộp chat. Vui lòng thử lại.';
                        return;
                    }

                    chatBtn.disabled = true;
                    chatBtn.classList.add('opacity-75');
                    chatNoteEl.textContent = 'Đang mở hộp chat...';

                    Promise.resolve(chatApi.openDirectChat(ownerAccountId, ownerName || null))
                        .then(function() {
                            chatNoteEl.textContent = ownerName
                                ? `Đã mở đoạn chat với ${ownerName}.`
                                : `Đã mở đoạn chat với ${ownerRoleText}.`;
                        })
                        .catch(function(err) {
                            chatNoteEl.textContent = err?.message || 'Không mở được hộp chat. Vui lòng thử lại.';
                        })
                        .finally(function() {
                            chatBtn.disabled = false;
                            chatBtn.classList.remove('opacity-75');
                        });
                };
            }

            function buildAutoDescription(item, isRoom) {
                const district = item.location?.district || 'khu vực phù hợp';
                const created = item.createdAt
                    ? new Date(item.createdAt).toLocaleDateString('vi-VN')
                    : 'gần đây';

                if (isRoom) {
                    const areaText = typeof item.area === 'number' ? `${item.area}m2` : 'diện tích linh hoạt';
                    const priceText = typeof item.price === 'number'
                        ? (item.price >= 1000000
                            ? `${(item.price / 1000000).toFixed(1)} triệu/tháng`
                            : `${item.price.toLocaleString('vi-VN')} đ/tháng`)
                        : 'mức giá thỏa thuận';
                    const statusText = item.roomStatus || 'còn trống';

                    return [
                        `Tin phòng trọ này nằm tại ${district}, phù hợp cho sinh viên hoặc người đi làm cần chỗ ở thuận tiện.`,
                        `Diện tích ${areaText}, mức giá ${priceText}, tình trạng hiện tại: ${statusText}.`,
                        `Bài đăng được cập nhật vào ${created}. Bạn có thể liên hệ trực tiếp để xem phòng và trao đổi thêm điều kiện thuê.`
                    ].join('\n');
                }

                const salaryText = item.salaryRange || 'mức lương thỏa thuận';
                const jobTypeText = item.jobType || 'hình thức làm việc linh hoạt';
                const statusText = item.jobStatus || 'đang tuyển';

                return [
                    `Vị trí việc làm này ở khu vực ${district}, phù hợp với ứng viên muốn tìm cơ hội làm việc ổn định và rõ ràng.`,
                    `Mức lương: ${salaryText}; loại công việc: ${jobTypeText}; trạng thái tuyển dụng: ${statusText}.`,
                    `Tin được đăng vào ${created}. Bạn có thể liên hệ ngay để xác nhận lịch làm, quyền lợi và yêu cầu công việc.`
                ].join('\n');
            }


            if (!id) {
                ['detail-skeleton-header', 'detail-skeleton-media', 'detail-skeleton-stats'].forEach(function(sid) {
                    var el = document.getElementById(sid); if(el) el.classList.add('d-none');
                });
                var errEl = document.getElementById('detail-error');
                if (errEl) errEl.classList.remove('d-none');
                return;
            }

            // Gọi API chi tiết
            const apiEndpoint = type === 'job' ? `/api/jobs/${id}` : `/api/rooms/${id}`;
            
            fetch(apiEndpoint)
                .then(res => {
                    if(!res.ok) throw new Error("API chưa trả về dữ liệu");
                    return res.json();
                })
                .then(json => {
                    var item = (json && json.success === true && json.data !== undefined) ? json.data : json;
                    // Cất đi thanh xương gánh
                    ['detail-skeleton-header', 'detail-skeleton-media', 'detail-skeleton-stats'].forEach(function(sid) {
                        var el = document.getElementById(sid); if(el) el.classList.add('d-none');
                    });
                    ['detail-header-content', 'detail-media-content', 'detail-stats-content'].forEach(function(cid) {
                        var el = document.getElementById(cid); if(el) el.classList.remove('d-none');
                    });

                    // Gắn dữ liệu vào giao diện
                    const isRoom = type === 'room';
                    const badgeEl = document.getElementById('detail-badge');
                    if (badgeEl) badgeEl.textContent = isRoom ? "Phòng Trọ" : "Việc Làm";

                    document.getElementById('detail-title').textContent = isRoom
                        ? (item.title || 'Phòng trọ')
                        : (item.jobTitle || 'Công việc');

                    const addressText = item.location?.addressText || item.address || item.companyName || 'Không rõ';
                    document.getElementById('detail-address').textContent = addressText;

                    const lat = Number(item.location?.lat ?? item.latitude);
                    const lng = Number(item.location?.lng ?? item.longitude);
                    initMiniMap(lat, lng, isRoom);

                    const categoryText = item.category || (isRoom ? 'Phòng trọ' : 'Việc làm');
                    document.getElementById('detail-category').textContent = categoryText;

                    const districtText = item.location?.district || 'Chưa cập nhật';
                    document.getElementById('detail-district').textContent = districtText;

                    const statusText = isRoom
                        ? (item.roomStatus || 'Chưa cập nhật')
                        : (item.jobStatus || 'Chưa cập nhật');
                    const statusEl = document.getElementById('detail-status');
                    if (statusEl) statusEl.textContent = statusText;

                    const createdText = item.createdAt
                        ? new Date(item.createdAt).toLocaleDateString('vi-VN')
                        : 'Chưa cập nhật';
                    const createdEl = document.getElementById('detail-created');
                    if (createdEl) createdEl.textContent = createdText;

                    const areaWrap = document.getElementById('detail-area-wrap');
                    const jobTypeWrap = document.getElementById('detail-jobtype-wrap');
                    if (isRoom) {
                        areaWrap.classList.remove('d-none');
                        jobTypeWrap.classList.add('d-none');
                        const areaText = typeof item.area === 'number' ? `${item.area} m²` : 'Chưa cập nhật';
                        document.getElementById('detail-area').textContent = areaText;
                    } else {
                        areaWrap.classList.add('d-none');
                        jobTypeWrap.classList.remove('d-none');
                        document.getElementById('detail-jobtype').textContent = item.jobType || 'Chưa cập nhật';
                    }

                    // Gắn giá/lương: ưu tiên field mới từ API detail, fallback về field cũ
                    let priceLabel = 'Thỏa thuận';
                    if (isRoom) {
                        if (typeof item.price === 'number') {
                            priceLabel = item.price >= 1000000
                                ? (item.price / 1000000).toFixed(1) + ' Triệu'
                                : item.price.toLocaleString('vi-VN') + ' đ';
                        } else if (typeof item.priceStr === 'number') {
                            priceLabel = item.priceStr >= 1000000
                                ? (item.priceStr / 1000000).toFixed(1) + ' Triệu'
                                : item.priceStr.toLocaleString('vi-VN') + ' đ';
                        }
                    } else {
                        priceLabel = item.salaryRange || item.salary || 'Thỏa thuận';
                    }
                    document.getElementById('detail-price').textContent = priceLabel;

                    // Hình ảnh
                    const fallbackImg = isRoom ? '/images/fallback-room.svg' : '/images/fallback-job.svg';
                    const detailImg = document.getElementById('detail-img');
                    detailImg.src = item.thumbnail || item.thumbnailUrl || item.thumbnaiUrl || fallbackImg;
                    detailImg.onerror = () => { detailImg.src = fallbackImg; };

                    // Ảnh phụ để page bớt trống và cho người dùng xem thêm góc nhìn
                    const mediaStrip = document.getElementById('detail-media-strip');
                    mediaStrip.innerHTML = '';
                    const mediaList = Array.isArray(item.media) ? item.media : [];
                    mediaList.slice(0, 6).forEach(function(m) {
                        const thumbImg = document.createElement('img');
                        thumbImg.src = m.mediaUrl || fallbackImg;
                        thumbImg.alt = 'Ảnh phụ';
                        thumbImg.style.cssText = 'width:92px;height:72px;object-fit:cover;border-radius:8px;border:1px solid var(--color-border);cursor:pointer;';
                        thumbImg.addEventListener('error', function() {
                            this.src = fallbackImg;
                        });
                        thumbImg.addEventListener('click', function() {
                            detailImg.src = this.src;
                        });
                        mediaStrip.appendChild(thumbImg);
                    });

                    // Ưu tiên mô tả do người đăng cung cấp, fallback sang mô tả tự động
                    var descEl = document.getElementById('detail-description');
                    var rawDesc = (item.description && item.description.trim())
                        ? item.description
                        : buildAutoDescription(item, isRoom);
                    // XSS-safe: escape HTML rồi mới thay \n thành <br>
                    descEl.innerHTML = window.escapeHtml(rawDesc).replace(/\n/g, '<br>');

                    if (item.contactPhone) {
                        document.getElementById('detail-phone').textContent = item.contactPhone;
                    }

                    const sourceLink = document.getElementById('detail-source-link');
                    if (sourceLink) {
                        sourceLink.classList.add('d-none');
                        sourceLink.removeAttribute('href');
                    }

                    configureContactAction(item, isRoom, id);
                    configureChatAction(item, isRoom);
                    loadReviews(type, id);
                    wireReviewForm(type, id);

                })
                .catch(err => {
                    console.error("fetch detail failed", err);
                    ['detail-skeleton-header', 'detail-skeleton-media', 'detail-skeleton-stats'].forEach(function(sid) {
                        var el = document.getElementById(sid); if(el) el.classList.add('d-none');
                    });
                    var errEl = document.getElementById('detail-error');
                    if (errEl) errEl.classList.remove('d-none');
                });

            // Bind back buttons (removed inline onclick)
            document.querySelectorAll('#detail-back-top, #detail-back-bottom').forEach(function(btn) {
                btn.addEventListener('click', function() { history.back(); });
            });
        });
