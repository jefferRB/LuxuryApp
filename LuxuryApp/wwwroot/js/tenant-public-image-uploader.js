(function () {
    if (window.__tenantPublicImageUploader) return;
    window.__tenantPublicImageUploader = true;

    const root = document.documentElement;
    root.classList.add('tppa-js');

    const uploadForms = Array.from(document.querySelectorAll('[data-public-image-upload]'));
    if (uploadForms.length === 0) return;

    const statusBox = document.querySelector('[data-public-image-status]');
    let active = null;
    let objectUrl = null;
    let modal = null;

    uploadForms.forEach(form => {
        const input = form.querySelector('input[type="file"][name="file"]');
        if (!input) return;

        input.addEventListener('change', () => {
            const file = input.files && input.files[0];
            if (!file) return;
            openCropper(form, input, file);
        });
    });

    document.querySelectorAll('[data-remove-form]').forEach(form => {
        form.addEventListener('submit', event => {
            event.preventDefault();
            submitRemove(form);
        });
    });

    function ensureModal() {
        if (modal) return modal;

        const wrapper = document.createElement('div');
        wrapper.className = 'tppa-crop-modal';
        wrapper.hidden = true;
        wrapper.innerHTML = [
            '<div class="tppa-crop-dialog" role="dialog" aria-modal="true" aria-label="Encuadrar imagen">',
            '  <div class="tppa-crop-head">',
            '    <div>',
            '      <h2>Encuadrar imagen</h2>',
            '      <p>Move la imagen y ajusta el zoom antes de subirla.</p>',
            '    </div>',
            '    <button type="button" class="tppa-crop-close" data-crop-cancel aria-label="Cerrar">x</button>',
            '  </div>',
            '  <div class="tppa-crop-stage">',
            '    <div class="tppa-crop-frame" data-crop-frame>',
            '      <img alt="" data-crop-image draggable="false" />',
            '    </div>',
            '  </div>',
            '  <label class="tppa-crop-zoom">Zoom',
            '    <input type="range" min="1" max="3" step="0.01" value="1" data-crop-zoom />',
            '  </label>',
            '  <div class="tppa-crop-error" data-crop-error hidden></div>',
            '  <div class="tppa-crop-actions">',
            '    <button type="button" class="tppa-btn tppa-btn-soft" data-crop-cancel>Cancelar</button>',
            '    <button type="button" class="tppa-btn tppa-btn-primary" data-crop-submit>Subir imagen</button>',
            '  </div>',
            '</div>'
        ].join('');

        document.body.appendChild(wrapper);

        modal = {
            wrapper,
            frame: wrapper.querySelector('[data-crop-frame]'),
            image: wrapper.querySelector('[data-crop-image]'),
            zoom: wrapper.querySelector('[data-crop-zoom]'),
            submit: wrapper.querySelector('[data-crop-submit]'),
            error: wrapper.querySelector('[data-crop-error]'),
            cancelButtons: Array.from(wrapper.querySelectorAll('[data-crop-cancel]'))
        };

        modal.cancelButtons.forEach(button => button.addEventListener('click', closeCropper));
        modal.zoom.addEventListener('input', () => {
            if (!active) return;
            active.zoom = parseFloat(modal.zoom.value) || 1;
            renderImage();
        });
        modal.submit.addEventListener('click', submitCrop);

        modal.frame.addEventListener('pointerdown', event => {
            if (!active) return;
            modal.frame.setPointerCapture(event.pointerId);
            active.drag = {
                x: event.clientX,
                y: event.clientY,
                offsetX: active.offsetX,
                offsetY: active.offsetY
            };
        });
        modal.frame.addEventListener('pointermove', event => {
            if (!active || !active.drag) return;
            active.offsetX = active.drag.offsetX + (event.clientX - active.drag.x);
            active.offsetY = active.drag.offsetY + (event.clientY - active.drag.y);
            renderImage();
        });
        modal.frame.addEventListener('pointerup', () => {
            if (active) active.drag = null;
        });
        modal.frame.addEventListener('pointercancel', () => {
            if (active) active.drag = null;
        });

        document.addEventListener('keydown', event => {
            if (event.key === 'Escape' && modal && !modal.wrapper.hidden) {
                closeCropper();
            }
        });

        return modal;
    }

    function openCropper(form, input, file) {
        const ui = ensureModal();
        revokeObjectUrl();
        objectUrl = URL.createObjectURL(file);

        const aspect = parseFloat(form.dataset.cropAspect || '1') || 1;
        ui.frame.style.aspectRatio = String(aspect);
        ui.zoom.value = '1';
        ui.error.hidden = true;
        ui.submit.disabled = false;
        ui.submit.textContent = 'Subir imagen';
        ui.image.src = objectUrl;

        active = {
            form,
            input,
            file,
            aspect,
            zoom: 1,
            offsetX: 0,
            offsetY: 0,
            baseScale: 1,
            drag: null,
            naturalWidth: 0,
            naturalHeight: 0
        };

        ui.image.onload = () => {
            if (!active) return;
            active.naturalWidth = ui.image.naturalWidth;
            active.naturalHeight = ui.image.naturalHeight;
            centerImage();
        };

        ui.wrapper.hidden = false;
        document.body.classList.add('tppa-crop-open');
    }

    function centerImage() {
        if (!active || !modal) return;
        const frame = modal.frame.getBoundingClientRect();
        active.baseScale = Math.max(
            frame.width / active.naturalWidth,
            frame.height / active.naturalHeight);
        active.offsetX = 0;
        active.offsetY = 0;
        renderImage();
    }

    function renderImage() {
        if (!active || !modal) return;

        const frame = modal.frame.getBoundingClientRect();
        const scale = active.baseScale * active.zoom;
        const displayWidth = active.naturalWidth * scale;
        const displayHeight = active.naturalHeight * scale;
        const maxOffsetX = Math.max(0, (displayWidth - frame.width) / 2);
        const maxOffsetY = Math.max(0, (displayHeight - frame.height) / 2);

        active.offsetX = clamp(active.offsetX, -maxOffsetX, maxOffsetX);
        active.offsetY = clamp(active.offsetY, -maxOffsetY, maxOffsetY);

        const left = (frame.width - displayWidth) / 2 + active.offsetX;
        const top = (frame.height - displayHeight) / 2 + active.offsetY;

        modal.image.style.width = `${displayWidth}px`;
        modal.image.style.height = `${displayHeight}px`;
        modal.image.style.transform = `translate(${left}px, ${top}px)`;
    }

    function calculateCrop() {
        const frame = modal.frame.getBoundingClientRect();
        const scale = active.baseScale * active.zoom;
        const displayWidth = active.naturalWidth * scale;
        const displayHeight = active.naturalHeight * scale;
        const left = (frame.width - displayWidth) / 2 + active.offsetX;
        const top = (frame.height - displayHeight) / 2 + active.offsetY;

        const cropX = clamp(Math.round((0 - left) / scale), 0, active.naturalWidth - 1);
        const cropY = clamp(Math.round((0 - top) / scale), 0, active.naturalHeight - 1);
        const cropWidth = clamp(Math.round(frame.width / scale), 1, active.naturalWidth - cropX);
        const cropHeight = clamp(Math.round(frame.height / scale), 1, active.naturalHeight - cropY);

        return { cropX, cropY, cropWidth, cropHeight };
    }

    async function submitCrop() {
        if (!active || !modal) return;

        const crop = calculateCrop();
        const formData = new FormData(active.form);
        formData.set('file', active.file);
        formData.set('CropX', String(crop.cropX));
        formData.set('CropY', String(crop.cropY));
        formData.set('CropWidth', String(crop.cropWidth));
        formData.set('CropHeight', String(crop.cropHeight));

        modal.submit.disabled = true;
        modal.submit.textContent = 'Subiendo...';
        modal.error.hidden = true;

        try {
            const response = await fetch(active.form.action, {
                method: 'POST',
                body: formData,
                headers: {
                    'X-Requested-With': 'XMLHttpRequest',
                    'Accept': 'application/json'
                }
            });

            const data = await response.json();
            if (!response.ok || !data.success) {
                throw new Error(data.message || 'No fue posible subir la imagen.');
            }

            applyUploadResult(active.form, data);
            showStatus(data.message || 'Imagen actualizada.', false);
            closeCropper();
        } catch (error) {
            modal.error.textContent = error.message || 'No fue posible subir la imagen.';
            modal.error.hidden = false;
            modal.submit.disabled = false;
            modal.submit.textContent = 'Subir imagen';
        }
    }

    async function submitRemove(form) {
        const formData = new FormData(form);
        try {
            const response = await fetch(form.action, {
                method: 'POST',
                body: formData,
                headers: {
                    'X-Requested-With': 'XMLHttpRequest',
                    'Accept': 'application/json'
                }
            });
            const data = await response.json();
            if (!response.ok || !data.success) {
                throw new Error(data.message || 'No fue posible quitar la imagen.');
            }

            applyRemoveResult(form, data);
            showStatus(data.message || 'Imagen removida.', false);
        } catch (error) {
            showStatus(error.message || 'No fue posible quitar la imagen.', true);
        }
    }

    function applyUploadResult(form, data) {
        updateUsage(data);

        if (form.dataset.uploadKind === 'gallery') {
            appendGalleryItem(form, data);
            updateGalleryCounters(form, data);
        } else {
            updateSingletonPreview(form, data);
        }

        form.reset();
    }

    function applyRemoveResult(form, data) {
        updateUsage(data);

        const thumb = form.closest('.tppa-thumb');
        if (thumb) {
            const list = thumb.parentElement;
            thumb.remove();
            if (list && list.children.length === 0) {
                list.hidden = true;
            }
            updateGalleryCounters(form, data);
            return;
        }

        const previewArea = form.closest('[data-preview-area]');
        const slot = form.closest('[data-public-image-slot]');
        if (previewArea && slot) {
            previewArea.innerHTML = `<div class="tppa-preview ${emptyPreviewClass(slot.dataset.publicImageSlot)} tppa-preview-empty" data-empty-preview>${emptyPreviewText(slot.dataset.publicImageSlot)}</div>`;
        }
    }

    function updateSingletonPreview(form, data) {
        const slot = form.closest('[data-public-image-slot]');
        if (!slot) return;

        const previewArea = slot.querySelector('[data-preview-area]');
        if (!previewArea) return;

        const previewClass = form.dataset.previewClass || 'tppa-preview-cover';
        const previewAlt = form.dataset.previewAlt || 'Imagen actual';
        const removeAction = resolveRemoveAction(form);
        const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
        const serviceId = form.querySelector('input[name="serviceId"]')?.value || '';
        const serviceIdInput = serviceId ? `<input type="hidden" name="serviceId" value="${escapeHtml(serviceId)}" />` : '';

        previewArea.innerHTML = [
            `<img class="tppa-preview ${previewClass}" src="${escapeAttribute(data.publicUrl)}" alt="${escapeAttribute(previewAlt)}" />`,
            `<small data-size-label>${escapeHtml(data.sizeLabel || '')}</small>`,
            `<form action="${escapeAttribute(removeAction)}" method="post" data-remove-form data-remove-target="singleton">`,
            `  <input type="hidden" name="__RequestVerificationToken" value="${escapeAttribute(token)}" />`,
            serviceIdInput,
            '  <button type="submit" class="tppa-btn tppa-btn-soft">Quitar imagen</button>',
            '</form>'
        ].join('');

        const removeForm = previewArea.querySelector('[data-remove-form]');
        if (removeForm) {
            removeForm.addEventListener('submit', event => {
                event.preventDefault();
                submitRemove(removeForm);
            });
        }
    }

    function appendGalleryItem(form, data) {
        const target = form.dataset.galleryListTarget;
        const list = target ? document.querySelector(`[data-gallery-list="${cssEscape(target)}"]`) : null;
        if (!list) return;

        const removeAction = resolveRemoveAction(form);
        const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
        const alt = form.dataset.previewAlt || 'Imagen de galeria';

        const item = document.createElement('div');
        item.className = 'tppa-thumb';
        item.dataset.assetId = data.assetId || '';
        item.innerHTML = [
            `<img src="${escapeAttribute(data.publicUrl)}" alt="${escapeAttribute(alt)}" />`,
            `<small data-size-label>${escapeHtml(data.sizeLabel || '')}</small>`,
            `<form action="${escapeAttribute(removeAction)}" method="post" data-remove-form data-remove-target="gallery">`,
            `  <input type="hidden" name="__RequestVerificationToken" value="${escapeAttribute(token)}" />`,
            `  <input type="hidden" name="assetId" value="${escapeAttribute(data.assetId || '')}" />`,
            '  <button type="submit" class="tppa-link-button">Quitar</button>',
            '</form>'
        ].join('');

        const removeForm = item.querySelector('[data-remove-form]');
        removeForm.addEventListener('submit', event => {
            event.preventDefault();
            submitRemove(removeForm);
        });

        list.hidden = false;
        list.appendChild(item);
    }

    function updateUsage(data) {
        const label = document.querySelector('[data-public-image-usage-label]');
        const bar = document.querySelector('[data-public-image-usage-bar]');
        if (label && data.usageLabel) label.textContent = data.usageLabel;
        if (bar && typeof data.usagePercent === 'number') bar.style.width = `${Math.min(data.usagePercent, 100)}%`;
    }

    function updateGalleryCounters(form, data) {
        const businessCounter = document.querySelector('[data-gallery-counter]');
        if (businessCounter && typeof data.businessGalleryCount === 'number') {
            businessCounter.textContent = `${data.businessGalleryCount} de ${data.maxBusinessGalleryImages} imagenes`;
        }

        const serviceMedia = form.closest('.tppa-service-media');
        const serviceCounter = serviceMedia && serviceMedia.querySelector('[data-service-gallery-counter]');
        if (serviceCounter && typeof data.serviceGalleryCount === 'number') {
            serviceCounter.textContent = `Galeria: ${data.serviceGalleryCount} de ${data.maxServiceGalleryImages}`;
        } else if (serviceCounter) {
            const list = serviceMedia.querySelector('[data-gallery-list]');
            const max = serviceCounter.dataset.galleryMax || '6';
            serviceCounter.textContent = `Galeria: ${list ? list.children.length : 0} de ${max}`;
        }
    }

    function resolveRemoveAction(form) {
        const action = form.action || window.location.href;
        return action
            .replace(/UploadLogo$/i, 'RemoveLogo')
            .replace(/UploadCover$/i, 'RemoveCover')
            .replace(/UploadBusinessGalleryImage$/i, 'RemoveBusinessGalleryImage')
            .replace(/UploadServiceMainImage$/i, 'RemoveServiceMainImage')
            .replace(/UploadServiceGalleryImage$/i, 'RemoveServiceGalleryImage');
    }

    function closeCropper() {
        if (!modal) return;
        modal.wrapper.hidden = true;
        document.body.classList.remove('tppa-crop-open');
        if (active && active.input) active.input.value = '';
        active = null;
        revokeObjectUrl();
    }

    function revokeObjectUrl() {
        if (objectUrl) {
            URL.revokeObjectURL(objectUrl);
            objectUrl = null;
        }
    }

    function showStatus(message, isError) {
        if (!statusBox) return;
        statusBox.textContent = message;
        statusBox.classList.toggle('tppa-ajax-status-error', Boolean(isError));
        statusBox.classList.toggle('tppa-ajax-status-ok', !isError);
        statusBox.hidden = false;
    }

    function emptyPreviewClass(slot) {
        if (slot === 'logo') return 'tppa-preview-logo';
        if (slot === 'service-main') return 'tppa-preview-service-main';
        return 'tppa-preview-cover';
    }

    function emptyPreviewText(slot) {
        if (slot === 'logo') return 'Sin logo';
        if (slot === 'service-main') return 'Sin imagen principal';
        return 'Sin portada';
    }

    function clamp(value, min, max) {
        return Math.min(Math.max(value, min), max);
    }

    function cssEscape(value) {
        if (window.CSS && CSS.escape) return CSS.escape(value);
        return String(value).replace(/"/g, '\\"');
    }

    function escapeHtml(value) {
        return String(value)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function escapeAttribute(value) {
        return escapeHtml(value);
    }
})();
