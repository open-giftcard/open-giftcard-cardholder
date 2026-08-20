/*
 * Optional progressive enhancements for the server-rendered cardholder app.
 *
 * This module must not fetch business data, handle an activation secret or
 * payment credential, or replace native links and forms. Removing it leaves
 * every journey complete.
 */

const root = document.documentElement;
const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
let submittingForms = new WeakSet();

root.classList.add("enhancements-active");

const menus = Array.from(document.querySelectorAll("details.menu"));

function closeMenu(menu, restoreFocus = false) {
  if (!menu.open) {
    return;
  }

  menu.open = false;
  if (restoreFocus) {
    menu.querySelector("summary")?.focus();
  }
}

for (const menu of menus) {
  menu.addEventListener("toggle", () => {
    if (!menu.open) {
      return;
    }

    for (const otherMenu of menus) {
      if (otherMenu !== menu) {
        closeMenu(otherMenu);
      }
    }
  });
}

document.addEventListener("pointerdown", (event) => {
  for (const menu of menus) {
    if (menu.open && !menu.contains(event.target)) {
      closeMenu(menu);
    }
  }
});

document.addEventListener("keydown", (event) => {
  if (event.key !== "Escape") {
    return;
  }

  const openMenu = menus.find((menu) => menu.open);
  if (openMenu) {
    event.preventDefault();
    closeMenu(openMenu, true);
  }
});

document.addEventListener("submit", (event) => {
  const form = event.target;
  if (!(form instanceof HTMLFormElement)) {
    return;
  }

  if (submittingForms.has(form)) {
    event.preventDefault();
    return;
  }

  submittingForms.add(form);
  form.classList.add("is-submitting");
  form.setAttribute("aria-busy", "true");

  if (event.submitter instanceof HTMLElement) {
    event.submitter.setAttribute("aria-disabled", "true");
  }
});

/* Browsers may restore a server-rendered page from their own history despite
 * cache headers. Never restore presentation-only busy state with it. */
window.addEventListener("pageshow", () => {
  submittingForms = new WeakSet();
  for (const form of document.querySelectorAll("form.is-submitting")) {
    form.classList.remove("is-submitting");
    form.removeAttribute("aria-busy");
    for (const control of form.querySelectorAll('[aria-disabled="true"]')) {
      control.removeAttribute("aria-disabled");
    }
  }
});

/* The CSS disclosure is authoritative. Script only keeps the newly revealed
 * sheet comfortably in the viewport on a small screen. */
document.addEventListener("change", (event) => {
  const input = event.target;
  if (!(input instanceof HTMLInputElement) ||
      !input.matches(".wallet-state--details") ||
      !input.checked) {
    return;
  }

  const sheet = input.parentElement?.querySelector(".sheet");
  if (!(sheet instanceof HTMLElement)) {
    return;
  }

  window.setTimeout(() => {
    const bounds = sheet.getBoundingClientRect();
    if (bounds.bottom <= window.innerHeight && bounds.top >= 0) {
      return;
    }

    sheet.scrollIntoView({
      behavior: reducedMotion.matches ? "auto" : "smooth",
      block: "nearest",
    });
  }, reducedMotion.matches ? 0 : 400);
});
