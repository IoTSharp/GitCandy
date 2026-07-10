(function () {
  "use strict";

  const themeStorageKey = "gitcandy-m9-prototype-theme";
  const themeValues = new Set(["system", "light", "dark"]);
  const documentElement = document.documentElement;
  const themeButtons = Array.from(document.querySelectorAll("[data-theme-value]"));
  const navigationToggle = document.querySelector(".navigation-toggle");
  const navigationCloseTargets = document.querySelectorAll("[data-navigation-close]");

  function setTheme(theme, persist) {
    const nextTheme = themeValues.has(theme) ? theme : "system";
    documentElement.dataset.theme = nextTheme;
    themeButtons.forEach(function (button) {
      button.setAttribute("aria-pressed", String(button.dataset.themeValue === nextTheme));
    });

    if (persist) {
      window.localStorage.setItem(themeStorageKey, nextTheme);
    }
  }

  function closeNavigation() {
    document.body.classList.remove("navigation-open");
    navigationToggle.setAttribute("aria-expanded", "false");
  }

  setTheme(window.localStorage.getItem(themeStorageKey) || "system", false);

  themeButtons.forEach(function (button) {
    button.addEventListener("click", function () {
      setTheme(button.dataset.themeValue, true);
    });
  });

  navigationToggle.addEventListener("click", function () {
    const isOpen = document.body.classList.toggle("navigation-open");
    navigationToggle.setAttribute("aria-expanded", String(isOpen));
  });

  navigationCloseTargets.forEach(function (target) {
    target.addEventListener("click", closeNavigation);
  });

  document.addEventListener("keydown", function (event) {
    if (event.key === "Escape" && document.body.classList.contains("navigation-open")) {
      closeNavigation();
      navigationToggle.focus();
    }
  });

  window.addEventListener("resize", function () {
    if (window.innerWidth > 900) {
      closeNavigation();
    }
  });
}());
