const currentQueryEl = document.getElementById('currentQuery');
const historyEl = document.getElementById('queryHistory');

// Función para crear tarjeta
function createCard(query) {
    const card = document.createElement('div');
    card.className = 'card';
    const now = new Date();
    card.innerHTML = `<div class="query">${query}</div><div class="date">${now.toLocaleString()}</div>`;
    return card;
}

function download(type, button) {
    const query = `?download=${type}`;
    currentQueryEl.textContent = query;

    const card = createCard(query);
    historyEl.prepend(card); 

    // Guardar historial en localStorage
    localStorage.setItem('queryHistory', historyEl.innerHTML);

    // Botón activo
    document.querySelectorAll('.buttons-container button').forEach(b => b.classList.remove('active'));
    button.classList.add('active');

    // Cambiar URL
    history.replaceState({}, '', query);
    window.location.href = query;
}

// Asignar evento a botones
document.getElementById('btnGzip').addEventListener('click', function(){ download('gzip', this); });
document.getElementById('btnSiteZip').addEventListener('click', function(){ download('sitezip', this); });

// Cargar historial desde localStorage al inicio
window.addEventListener('load', () => {
    if (localStorage.getItem('queryHistory')) {
        historyEl.innerHTML = localStorage.getItem('queryHistory');
    }

    const qs = window.location.search;
    if (qs) {
        currentQueryEl.textContent = qs;
        const card = createCard(qs);
        historyEl.prepend(card);
        localStorage.setItem('queryHistory', historyEl.innerHTML);

        if (qs.includes('gzip')) document.getElementById('btnGzip').classList.add('active');
        if (qs.includes('sitezip')) document.getElementById('btnSiteZip').classList.add('active');
    }
});


document.getElementById("postForm").addEventListener("submit", async (e) => {
    e.preventDefault();

    const data = document.getElementById("postInput").value;

    await fetch("/enviar", {
        method: "POST",
        headers: { "Content-Type": "text/plain" },
        body: data
    });

    document.getElementById("lastPostResponse").textContent = "Último POST: " + data;
    document.getElementById("postInput").value = "";
});
