const canvas = document.getElementById('worldCanvas');
const ctx = canvas.getContext('2d');
const statusText = document.getElementById('status');

let CELL_SIZE = 15;
let worldWidth = 0;
let worldHeight = 0;

let staticBlocks = [];
let foods = [];
let creaturesMap = new Map();

const ws = new WebSocket('ws://localhost:8080');

ws.onopen = () => statusText.innerText = "NEMO Engine Active";
ws.onclose = () => statusText.innerText = "Connection Lost.";

ws.onmessage = (event) => {
    const data = JSON.parse(event.data);

    if (worldWidth !== data.width || worldHeight !== data.height) {
        worldWidth = data.width;
        worldHeight = data.height;
        canvas.width = worldWidth * CELL_SIZE;
        canvas.height = worldHeight * CELL_SIZE;
    }

    staticBlocks = data.blocks;
    foods = data.foods;

    const currentIDs = new Set();

    data.creatures.forEach(c => {
        currentIDs.add(c.id);

        const targetAngle = (c.dir - 2) * (Math.PI / 4);

        if (!creaturesMap.has(c.id)) {
            creaturesMap.set(c.id, {
                x: c.x, y: c.y,
                targetX: c.x, targetY: c.y,
                angle: targetAngle, targetAngle: targetAngle,
                r: c.r, g: c.g, b: c.b
            });
        } else {
            let existing = creaturesMap.get(c.id);
            existing.targetX = c.x;
            existing.targetY = c.y;

            existing.targetAngle = targetAngle;
            while (existing.targetAngle - existing.angle > Math.PI) existing.targetAngle -= Math.PI * 2;
            while (existing.targetAngle - existing.angle < -Math.PI) existing.targetAngle += Math.PI * 2;
        }
    });

    for (let id of creaturesMap.keys()) {
        if (!currentIDs.has(id)) {
            creaturesMap.delete(id);
        }
    }
};

function lerp(start, end, factor) {
    return start + (end - start) * factor;
}

function drawCreature(ctx, r, g, b) {
    const size = CELL_SIZE * 0.8;

    ctx.fillStyle = `rgb(${r}, ${g}, ${b})`;
    ctx.strokeStyle = `rgb(${r}, ${g}, ${b})`;
    ctx.lineWidth = 2;
    ctx.lineJoin = "round";

    ctx.beginPath();
    ctx.moveTo(size / 2, 0); 
    ctx.lineTo(-size / 2, size / 2.5); 
    ctx.quadraticCurveTo(-size / 4, 0, -size / 2, -size / 2.5); 
    ctx.closePath();

    ctx.fill();
    ctx.stroke();
}

function render() {
    ctx.fillStyle = '#1e1e1e';
    ctx.fillRect(0, 0, canvas.width, canvas.height);

    ctx.fillStyle = '#000000';
    staticBlocks.forEach(b => {
        ctx.fillRect(b.x * CELL_SIZE, b.y * CELL_SIZE, CELL_SIZE, CELL_SIZE);
    });

    foods.forEach(f => {
        ctx.beginPath();
        ctx.arc((f.x * CELL_SIZE) + (CELL_SIZE / 2), (f.y * CELL_SIZE) + (CELL_SIZE / 2), CELL_SIZE * 0.35, 0, Math.PI * 2);
        ctx.fillStyle = f.meat ? '#800000' : '#006400';
        ctx.fill();
    });

    creaturesMap.forEach(c => {
        c.x = lerp(c.x, c.targetX, 0.2);
        c.y = lerp(c.y, c.targetY, 0.2);
        c.angle = lerp(c.angle, c.targetAngle, 0.2);

        ctx.save();
        ctx.translate((c.x * CELL_SIZE) + (CELL_SIZE / 2), (c.y * CELL_SIZE) + (CELL_SIZE / 2));
        ctx.rotate(c.angle);

        drawCreature(ctx, c.r, c.g, c.b);

        ctx.restore();
    });

    requestAnimationFrame(render);
}

requestAnimationFrame(render);