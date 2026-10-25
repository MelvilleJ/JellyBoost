(() => {
    'use strict';

    if (window.__jellyBoostLoaded) {
        return;
    }
    window.__jellyBoostLoaded = true;

    const STEP_DB = 1;
    const SAVE_DELAY_MS = 500;

    // Keep in sync with Boost/AudioFilters.cs so browser and server boosts sound alike.
    const NIGHT = { threshold: -30, knee: 6, ratio: 4, attack: 0.005, release: 0.25, makeup: 2 };
    const LIMITER = { threshold: -0.5, knee: 0, ratio: 20, attack: 0.001, release: 0.1 };

    const AudioContextClass = window.AudioContext || window.webkitAudioContext;

    const state = {
        userId: null,
        settings: null,
        local: false,
        audioContext: null,
        graphs: new WeakMap(),
        currentGraph: null,
        saveTimer: null
    };

    const api = () => window.ApiClient;

    function request(method, path, body) {
        const client = api();
        return client.ajax({
            type: method,
            url: client.getUrl(path),
            data: body ? JSON.stringify(body) : undefined,
            contentType: body ? 'application/json' : undefined,
            dataType: path === 'JellyBoost/Settings' ? 'json' : undefined
        });
    }

    // Web Audio on a <video> is only safe where the browser decodes audio itself. On iOS/Safari
    // (native HLS), TVs and native app shells it can mute playback, so those use the server boost.
    function supportsLocalBoost() {
        if (!AudioContextClass || window.NativeShell || window.jmpInfo) {
            return false;
        }

        const ua = navigator.userAgent;
        if (/Web0S|WebOS|Tizen|SMART-TV|SmartTV|HbbTV|CrKey|Roku|AFT[A-Z]|PlayStation|Xbox|Nintendo/i.test(ua)) {
            return false;
        }

        const iOS = /iPad|iPhone|iPod/.test(ua) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
        const safari = /^((?!chrome|chromium|android|crios|fxios|edg).)*safari/i.test(ua);
        if (iOS || safari) {
            return false;
        }

        // Cross-origin media without CORS headers would play silently through Web Audio.
        try {
            return new URL(api().serverAddress()).origin === window.location.origin;
        } catch {
            return false;
        }
    }

    function isActive(settings) {
        return !!settings && (settings.GainDb > 0 || settings.NightMode);
    }

    async function onUserChanged(userId) {
        state.userId = userId;
        state.settings = null;
        if (!userId) {
            return;
        }

        state.local = supportsLocalBoost();
        try {
            await request('POST', 'JellyBoost/ClientBoost', { Enabled: state.local });
            await refreshSettings();
        } catch (err) {
            console.warn('[JellyBoost] could not reach the server plugin', err);
        }
    }

    async function refreshSettings() {
        state.settings = await request('GET', 'JellyBoost/Settings');
        applyToGraph(state.currentGraph);
        renderPanel();
    }

    function scheduleSave() {
        clearTimeout(state.saveTimer);
        state.saveTimer = setTimeout(async () => {
            try {
                state.settings = await request('POST', 'JellyBoost/Settings', {
                    GainDb: state.settings.GainDb,
                    NightMode: state.settings.NightMode
                });
                renderPanel();
            } catch (err) {
                console.warn('[JellyBoost] could not save settings', err);
            }
        }, SAVE_DELAY_MS);
    }

    function getAudioContext() {
        if (!state.audioContext) {
            state.audioContext = new AudioContextClass();
        }
        return state.audioContext;
    }

    function setParams(node, params) {
        for (const [key, value] of Object.entries(params)) {
            if (node[key] instanceof AudioParam) {
                node[key].value = value;
            }
        }
    }

    function buildGraph(video) {
        const ctx = getAudioContext();
        const graph = {
            source: ctx.createMediaElementSource(video),
            night: ctx.createDynamicsCompressor(),
            makeup: ctx.createGain(),
            gain: ctx.createGain(),
            limiter: ctx.createDynamicsCompressor(),
            nightConnected: null
        };
        setParams(graph.night, NIGHT);
        graph.makeup.gain.value = NIGHT.makeup;
        setParams(graph.limiter, LIMITER);

        graph.night.connect(graph.makeup);
        graph.makeup.connect(graph.gain);
        graph.gain.connect(graph.limiter);
        graph.limiter.connect(ctx.destination);
        return graph;
    }

    function applyToGraph(graph) {
        if (!graph || !state.settings) {
            return;
        }

        const night = !!state.settings.NightMode;
        if (graph.nightConnected !== night) {
            graph.source.disconnect();
            graph.source.connect(night ? graph.night : graph.gain);
            graph.nightConnected = night;
        }

        graph.gain.gain.setTargetAtTime(Math.pow(10, state.settings.GainDb / 20), getAudioContext().currentTime, 0.05);
    }

    // Once a <video> is routed into Web Audio it stays silent unless the context is running,
    // so only attach after the context has actually started.
    async function attachLocalBoost(video) {
        if (!state.local || !isActive(state.settings) || state.graphs.has(video)) {
            state.currentGraph = state.graphs.get(video) || null;
            applyToGraph(state.currentGraph);
            return;
        }

        const ctx = getAudioContext();
        if (ctx.state !== 'running') {
            try {
                await ctx.resume();
            } catch {
                // Falls through to the state check below.
            }
        }
        if (ctx.state !== 'running' || state.graphs.has(video)) {
            return;
        }

        try {
            const graph = buildGraph(video);
            state.graphs.set(video, graph);
            state.currentGraph = graph;
            applyToGraph(graph);
        } catch (err) {
            console.warn('[JellyBoost] Web Audio boost unavailable', err);
        }
    }

    function onVideoElement(video) {
        if (video.__jellyBoostBound) {
            return;
        }
        video.__jellyBoostBound = true;

        video.addEventListener('playing', () => {
            attachLocalBoost(video);
        });

        refreshSettings()
            .catch(() => undefined)
            .finally(() => attachLocalBoost(video));
    }

    function formatGain(gainDb) {
        const factor = Math.pow(10, gainDb / 20);
        return gainDb > 0 ? `+${gainDb} dB (${factor.toFixed(1)}x)` : 'Off';
    }

    function statusText() {
        const s = state.settings;
        if (!s) {
            return 'Loading...';
        }
        if (state.local) {
            return 'Applied instantly in this browser.';
        }
        if (!s.ServerSideBoostEnabled) {
            return 'Server-side boost is turned off by your administrator.';
        }
        return 'Applied by the server. Changes take effect the next time playback starts.';
    }

    function changeGain(deltaDb) {
        const s = state.settings;
        if (!s) {
            return;
        }
        s.GainDb = Math.min(s.MaxGainDb, Math.max(0, Math.round(s.GainDb + deltaDb)));
        onSettingsEdited();
    }

    function toggleNight() {
        if (!state.settings) {
            return;
        }
        state.settings.NightMode = !state.settings.NightMode;
        onSettingsEdited();
    }

    function onSettingsEdited() {
        const video = document.querySelector('video.htmlvideoplayer');
        if (video) {
            attachLocalBoost(video);
        }
        applyToGraph(state.currentGraph);
        renderPanel();
        scheduleSave();
    }

    function injectStyles() {
        if (document.getElementById('jellyBoostStyles')) {
            return;
        }
        const style = document.createElement('style');
        style.id = 'jellyBoostStyles';
        style.textContent = `
            .jellyBoostPanel { position: absolute; right: 1em; bottom: 100%; margin-bottom: .5em; z-index: 10;
                min-width: 17em; padding: 1em; border-radius: .5em; background: rgba(20, 20, 20, .95); color: #fff;
                box-shadow: 0 .3em 1em rgba(0, 0, 0, .5); pointer-events: auto; }
            .jellyBoostPanel h3 { margin: 0 0 .75em; font-size: 1.1em; }
            .jellyBoostRow { display: flex; align-items: center; gap: .5em; margin-bottom: .75em; }
            .jellyBoostValue { flex: 1; text-align: center; font-size: 1.1em; }
            .jellyBoostPanel button { margin: 0; }
            .jellyBoostStep { min-width: 3em; font-size: 1.3em; }
            .jellyBoostWide { width: 100%; margin-bottom: .5em !important; }
            .jellyBoostStatus { font-size: .85em; opacity: .75; margin: .25em 0 .75em; max-width: 17em; }
            .btnJellyBoost.jellyBoostOn .material-icons { color: #00a4dc; }
        `;
        document.head.appendChild(style);
    }

    function makeButton(className, text, onClick) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = `emby-button raised ${className}`;
        button.textContent = text;
        button.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            onClick();
        });
        return button;
    }

    function closePanel() {
        const panel = document.querySelector('.jellyBoostPanel');
        if (panel) {
            panel.remove();
            document.querySelector('.btnJellyBoost')?.focus();
        }
    }

    function openPanel(osdBottom) {
        if (document.querySelector('.jellyBoostPanel')) {
            closePanel();
            return;
        }

        const panel = document.createElement('div');
        panel.className = 'jellyBoostPanel focuscontainer';

        const title = document.createElement('h3');
        title.textContent = 'Volume boost';

        const row = document.createElement('div');
        row.className = 'jellyBoostRow';
        const value = document.createElement('div');
        value.className = 'jellyBoostValue';
        row.append(
            makeButton('jellyBoostStep jellyBoostDown', '−', () => changeGain(-STEP_DB)),
            value,
            makeButton('jellyBoostStep jellyBoostUp', '+', () => changeGain(STEP_DB)));

        const night = makeButton('jellyBoostWide jellyBoostNight', '', toggleNight);
        const status = document.createElement('div');
        status.className = 'jellyBoostStatus';
        const done = makeButton('jellyBoostWide button-submit', 'Done', closePanel);

        panel.append(title, row, night, status, done);
        panel.addEventListener('click', (e) => e.stopPropagation());
        osdBottom.appendChild(panel);
        renderPanel();
        panel.querySelector('.jellyBoostUp').focus();
    }

    function renderPanel() {
        const s = state.settings;
        document.querySelector('.btnJellyBoost')?.classList.toggle('jellyBoostOn', isActive(s));

        const panel = document.querySelector('.jellyBoostPanel');
        if (!panel) {
            return;
        }
        panel.querySelector('.jellyBoostValue').textContent = s ? formatGain(s.GainDb) : '...';
        panel.querySelector('.jellyBoostNight').textContent = `Night mode: ${s?.NightMode ? 'On' : 'Off'}`;
        panel.querySelector('.jellyBoostStatus').textContent = statusText();
    }

    function addOsdButton(buttons) {
        if (buttons.querySelector('.btnJellyBoost')) {
            return;
        }
        injectStyles();

        // Built from markup because jellyfin-web's custom-elements polyfill rejects createElement(name, { is }).
        const template = document.createElement('div');
        template.innerHTML = '<button is="paper-icon-button-light" type="button" class="btnJellyBoost autoSize" title="Volume boost" aria-label="Volume boost">'
            + '<span class="xlargePaperIconButton material-icons graphic_eq" aria-hidden="true"></span></button>';
        const button = template.firstElementChild;
        button.addEventListener('click', (e) => {
            e.stopPropagation();
            openPanel(buttons.closest('.videoOsdBottom'));
        });

        const anchor = buttons.querySelector('.btnVideoOsdSettings');
        buttons.insertBefore(button, anchor || null);
        renderPanel();
    }

    function scan() {
        const client = api();
        const userId = client?.getCurrentUserId?.() || null;
        if (userId !== state.userId) {
            onUserChanged(userId);
        }
        if (!userId) {
            return;
        }

        document.querySelectorAll('video.htmlvideoplayer').forEach(onVideoElement);
        document.querySelectorAll('.videoOsdBottom .buttons').forEach(addOsdButton);
    }

    let scanQueued = false;
    new MutationObserver(() => {
        if (!scanQueued) {
            scanQueued = true;
            requestAnimationFrame(() => {
                scanQueued = false;
                scan();
            });
        }
    }).observe(document.documentElement, { childList: true, subtree: true });

    scan();
})();
