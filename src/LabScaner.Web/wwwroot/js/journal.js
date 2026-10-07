// Журнал группы: всплывающие окна (быстрая отметка, ввод экзамена), панель и диалог.
// Логика — на сервере (htmx); здесь только открыть/закрыть.
(function () {
  "use strict";

  function closePops(except) {
    document.querySelectorAll(".pop").forEach(function (pop) {
      if (pop !== except) {
        pop.innerHTML = "";
      }
    });
  }

  document.addEventListener("htmx:beforeRequest", function (e) {
    var target = e.detail.target;
    if (target && target.classList && target.classList.contains("pop")) {
      closePops(target);
    }
  });

  document.addEventListener("click", function (e) {
    var t = e.target;
    if (t.closest("[data-close-pop]")) {
      closePops(null);
      return;
    }
    if (t.closest("[data-close-modal]")) {
      document.getElementById("modal").innerHTML = "";
      return;
    }
    if (t.closest("[data-close-panel]")) {
      document.getElementById("panel").innerHTML = "";
      return;
    }
    if (!t.closest(".pop") && !t.closest(".cell") && !t.closest(".final")) {
      closePops(null);
    }
  });

  document.addEventListener("keydown", function (e) {
    if (e.key === "Escape") {
      closePops(null);
      var modal = document.getElementById("modal");
      if (modal) {
        modal.innerHTML = "";
      }
    }
  });

  // Карточка открылась — закрыть всплывающее окно и показать карточку (на телефоне она ниже таблицы).
  document.addEventListener("htmx:afterSwap", function (e) {
    if (e.detail.target.id === "panel" && e.detail.target.innerHTML.trim() !== "") {
      closePops(null);
      if (window.matchMedia("(max-width: 1100px)").matches) {
        e.detail.target.scrollIntoView({ behavior: "smooth", block: "start" });
      }
    }
  });
})();
