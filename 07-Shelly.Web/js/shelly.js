async function request(path) {
    const response = await fetch(path);

    if (!response.ok) {
        throw new Error(
            `Shelly HTTP error: ${response.status}`
        );
    }

    return response.json();
}

export async function getLightStatus() {
    return request("/rpc/Switch.GetStatus?id=0");
}

export async function turnOnLight() {
    await request("/rpc/Switch.Set?id=0&on=true");

    return getLightStatus();
}

export async function turnOffLight() {
    await request("/rpc/Switch.Set?id=0&on=false");

    return getLightStatus();
}
