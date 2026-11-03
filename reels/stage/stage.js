// The stage's one control. Each .beat shows alone; inside it, every [data-at="n"] element gets .in once the beat has
// reached step n ([data-now] only while it is step n; [data-gone="n"] gets .gone from step n). A click on #next, or the right arrow, takes the next step, then the
// next beat. Open page.html#<beat id> to start on a beat, page.html#<beat id>/<step> on a step of it.
(() => {
	const beats = [...document.querySelectorAll(".beat")];
	const steps = el => Math.max(0, ...[...el.querySelectorAll("[data-at]")].map(x => +x.dataset.at));
	let b = 0, s = 0;

	function show() {
		beats.forEach((el, i) => {
			el.classList.toggle("on", i === b);
			el.classList.toggle("past", i < b);
		});
		const el = beats[b];
		el.dataset.step = s;
		el.querySelectorAll("[data-at]").forEach(x => x.classList.toggle("in", "now" in x.dataset ? +x.dataset.at === s : +x.dataset.at <= s));
		el.querySelectorAll("[data-gone]").forEach(x => x.classList.toggle("gone", +x.dataset.gone <= s));
		el.dispatchEvent(new CustomEvent("step", { detail: s }));
	}

	function next() {
		if (s < steps(beats[b])) s++;
		else if (b < beats.length - 1) { b++; s = 0; }
		show();
	}

	function back() {
		if (s > 0) s--;
		else if (b > 0) { b--; s = steps(beats[b]); }
		show();
	}

	const [id, step] = decodeURIComponent(location.hash.slice(1)).split("/");
	const at = beats.findIndex(el => el.id === id);
	if (at >= 0) { b = at; s = Math.min(+step || 0, steps(beats[at])); }

	document.getElementById("next").addEventListener("click", next);
	addEventListener("keydown", e => {
		if (e.key === "ArrowRight" || e.key === " ") next();
		if (e.key === "ArrowLeft") back();
	});
	show();
})();
