import {
    getLightStatus,
    turnOnLight,
    turnOffLight
} from "./shelly.js";

const statusElement = document.getElementById("status");
const powerElement = document.getElementById("power");
const indicatorElement = document.getElementById("indicator");
const errorElement = document.getElementById("error");

function render(status) {
    statusElement.textContent =
        status.output ? "Włączona" : "Wyłączona";

    powerElement.textContent =
        Number(status.apower).toFixed(2);

    indicatorElement.classList.toggle(
        "on",
        status.output
    );

    errorElement.textContent = "";
}

async function execute(action) {
    try {
        errorElement.textContent = "";

        const status = await action();

        render(status);
    } catch (error) {
        errorElement.textContent = error.message;
    }
}

document.getElementById("turn-on")
    .addEventListener("click", () => execute(turnOnLight));

document.getElementById("turn-off")
    .addEventListener("click", () => execute(turnOffLight));

document.getElementById("refresh")
    .addEventListener("click", () => execute(getLightStatus));

execute(getLightStatus);
