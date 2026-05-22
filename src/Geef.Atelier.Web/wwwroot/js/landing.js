(function () {
  'use strict';

  var reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  if (reduced) document.body.setAttribute('data-static', '1');

  // Scroll reveals — add .in when element crosses threshold
  var revealObs = new IntersectionObserver(function (entries) {
    entries.forEach(function (e) {
      if (e.isIntersecting) {
        e.target.classList.add('in');
        revealObs.unobserve(e.target);
      }
    });
  }, { threshold: 0.18 });

  document.querySelectorAll('.lp-reveal:not(.geef-flow)').forEach(function (el) {
    if (reduced) { el.classList.add('in'); } else { revealObs.observe(el); }
  });

  // Film — click the poster overlay to start the demo with sound
  var filmStage = document.querySelector('.lp-film-stage');
  if (filmStage) {
    var filmVideo = filmStage.querySelector('.lp-film-video');
    var filmPlay = filmStage.querySelector('.lp-film-play');
    if (filmVideo && filmPlay) {
      filmPlay.addEventListener('click', function () {
        filmStage.classList.add('playing');
        filmVideo.controls = true;   // reveal native controls only once playing
        filmVideo.play();
      });
      filmVideo.addEventListener('play', function () {
        filmStage.classList.add('playing');
        filmVideo.controls = true;
      });
    }
  }

  // GEEF flow choreography — phases G → E → E-loop → F
  var flow = document.querySelector('.geef-flow');
  if (!flow) return;

  var phases = flow.querySelectorAll('.geef-phase');

  function setPhase(p) {
    phases.forEach(function (ph, i) {
      ph.classList.remove('is-active', 'has-passed');
      if (p > i) ph.classList.add('has-passed');
      if (p === i || (i === 2 && p >= 2 && p < 3)) ph.classList.add('is-active');
    });
  }

  function wait(ms) { return new Promise(function (r) { setTimeout(r, ms); }); }

  async function runSequence() {
    await wait(280);
    flow.classList.add('run');
    setPhase(0);
    await wait(900);
    setPhase(1);
    await wait(900);
    setPhase(2);
    await wait(2400);
    setPhase(3);
    await wait(800);
    phases.forEach(function (ph) { ph.classList.remove('is-active'); ph.classList.add('has-passed'); });
  }

  if (reduced) {
    flow.classList.add('run');
    phases.forEach(function (ph) { ph.classList.add('has-passed'); });
    return;
  }

  var geefStarted = false;
  var geefObs = new IntersectionObserver(function (entries) {
    entries.forEach(function (e) {
      if (e.isIntersecting && !geefStarted) {
        geefStarted = true;
        geefObs.unobserve(e.target);
        runSequence();
      }
    });
  }, { threshold: 0.32 });

  geefObs.observe(flow);
})();
