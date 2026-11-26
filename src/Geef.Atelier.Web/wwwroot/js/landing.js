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

  document.querySelectorAll('.lp-reveal').forEach(function (el) {
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

      // Language toggle — swap source + poster, reset to the poster state.
      // The buttons live in .lp-film-frame, a sibling wrapper above the stage.
      var filmScope = filmStage.closest('.lp-film-frame') || document;
      var langBtns = filmScope.querySelectorAll('.lp-film-lang');
      var filmSource = filmVideo.querySelector('source');
      langBtns.forEach(function (btn) {
        btn.addEventListener('click', function () {
          var lang = btn.getAttribute('data-lang');
          var src = filmVideo.getAttribute('data-src-' + lang);
          var poster = filmVideo.getAttribute('data-poster-' + lang);
          if (!src || btn.classList.contains('is-active')) return;

          var wasPlaying = !filmVideo.paused && !filmVideo.ended;
          filmVideo.pause();
          if (filmSource) filmSource.setAttribute('src', src);
          if (poster) filmVideo.setAttribute('poster', poster);
          filmVideo.load();

          langBtns.forEach(function (b) {
            var on = b === btn;
            b.classList.toggle('is-active', on);
            b.setAttribute('aria-pressed', on ? 'true' : 'false');
          });

          if (wasPlaying) {
            filmStage.classList.add('playing');
            filmVideo.controls = true;
            filmVideo.play();
          } else {
            filmStage.classList.remove('playing');
            filmVideo.controls = false;
          }
        });
      });
    }
  }

  // Steps choreography: one pass over the steps of the standard way, the
  // "one more try" arc shows while the check step is active.
  var flow = document.querySelector('.lp-flow');
  if (!flow) return;

  var steps = Array.prototype.slice.call(flow.querySelectorAll('.lp-flow-step'));

  function setStep(p) {
    steps.forEach(function (st, i) {
      st.classList.toggle('has-passed', p > i);
      st.classList.toggle('is-active', p === i);
    });
  }

  function wait(ms) { return new Promise(function (r) { setTimeout(r, ms); }); }

  async function runSequence() {
    await wait(240);
    flow.classList.add('run');
    for (var i = 0; i < steps.length; i++) {
      setStep(i);
      var isCheck = steps[i].getAttribute('data-step') === 'check';
      if (isCheck) flow.classList.add('retrying');
      await wait(isCheck ? 1800 : 800);
      if (isCheck) flow.classList.remove('retrying');
    }
    setStep(steps.length);
  }

  if (reduced) {
    flow.classList.add('run');
    setStep(steps.length);
    return;
  }

  var flowStarted = false;
  var flowObs = new IntersectionObserver(function (entries) {
    entries.forEach(function (e) {
      if (e.isIntersecting && !flowStarted) {
        flowStarted = true;
        flowObs.unobserve(e.target);
        runSequence();
      }
    });
  }, { threshold: 0.15 });

  flowObs.observe(flow);
})();
