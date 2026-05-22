const fs = require('fs');
const path = require('path');

function generateCirclePolygon(centerLng, centerLat, radius, numSides = 16) {
    const points = [];
    for (let i = 0; i <= numSides; i++) {
        const angle = (i * 2 * Math.PI) / numSides;
        const lng = centerLng + radius * Math.cos(angle);
        const lat = centerLat + radius * Math.sin(angle);
        points.push([lng, lat]);
    }
    return [ points ];
}

function run() {
    const jsonPath = path.join(__dirname, '..', 'wwwroot', 'data', 'vietnam.json');
    if (!fs.existsSync(jsonPath)) {
        console.error("vietnam.json not found!");
        return;
    }

    console.log("Reading vietnam.json...");
    const rawData = fs.readFileSync(jsonPath, 'utf8');
    const data = JSON.parse(rawData);

    console.log("Original polygons count:", data.coordinates.length);

    // Define 6 key features/islands in Hoang Sa (Paracels)
    const features = [
        { name: "Đảo Tri Tôn (Triton Island)", centerLng: 111.201, centerLat: 15.785, radius: 0.04 },
        { name: "Nhóm Lưỡi Liềm (Crescent Group)", centerLng: 111.68, centerLat: 16.51, radius: 0.15 },
        { name: "Nhóm An Vĩnh (Amphitrite Group)", centerLng: 112.33, centerLat: 16.89, radius: 0.15 },
        { name: "Đảo Linh Côn (Lincoln Island)", centerLng: 112.734, centerLat: 16.668, radius: 0.05 },
        { name: "Đá Bông Bay (Bombay Reef)", centerLng: 112.51, centerLat: 16.04, radius: 0.06 },
        { name: "Đá Đầu Lính (Discovery Reef)", centerLng: 111.68, centerLat: 16.23, radius: 0.07 }
    ];

    features.forEach(f => {
        console.log(`Adding feature: ${f.name} at [${f.centerLng}, ${f.centerLat}] with radius ${f.radius}`);
        const poly = generateCirclePolygon(f.centerLng, f.centerLat, f.radius);
        data.coordinates.push(poly);
    });

    console.log("New polygons count:", data.coordinates.length);

    // Save backup first
    const backupPath = jsonPath + '.bak';
    if (!fs.existsSync(backupPath)) {
        fs.writeFileSync(backupPath, rawData, 'utf8');
        console.log("Saved backup to:", backupPath);
    }

    // Write updated GeoJSON
    fs.writeFileSync(jsonPath, JSON.stringify(data), 'utf8');
    console.log("Successfully updated vietnam.json!");
}

run();
