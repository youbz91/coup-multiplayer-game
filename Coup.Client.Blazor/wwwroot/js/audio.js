// Audio Service for Coup Game
// Uses Web Audio API to generate simple sound effects

const audioContext = new (window.AudioContext || window.webkitAudioContext)();

// Sound generation functions
function playSound(soundName, volume = 0.5) {
    try {
        switch (soundName) {
            case 'click':
                playClick(volume);
                break;
            case 'coin-gain':
                playCoinGain(volume);
                break;
            case 'coin-loss':
                playCoinLoss(volume);
                break;
            case 'card-flip':
                playCardFlip(volume);
                break;
            case 'action-success':
                playActionSuccess(volume);
                break;
            case 'action-blocked':
                playActionBlocked(volume);
                break;
            case 'timer-warning':
                playTimerWarning(volume);
                break;
            case 'influence-loss':
                playInfluenceLoss(volume);
                break;
            case 'challenge':
                playChallenge(volume);
                break;
        }
    } catch (error) {
        console.error('Error playing sound:', error);
    }
}

// Button click sound
function playClick(volume) {
    const oscillator = audioContext.createOscillator();
    const gainNode = audioContext.createGain();

    oscillator.connect(gainNode);
    gainNode.connect(audioContext.destination);

    oscillator.frequency.value = 800;
    oscillator.type = 'sine';

    gainNode.gain.setValueAtTime(volume * 0.3, audioContext.currentTime);
    gainNode.gain.exponentialRampToValueAtTime(0.01, audioContext.currentTime + 0.1);

    oscillator.start(audioContext.currentTime);
    oscillator.stop(audioContext.currentTime + 0.1);
}

// Coin gain sound (ascending notes)
function playCoinGain(volume) {
    const notes = [523.25, 659.25, 783.99]; // C, E, G
    notes.forEach((freq, index) => {
        const oscillator = audioContext.createOscillator();
        const gainNode = audioContext.createGain();

        oscillator.connect(gainNode);
        gainNode.connect(audioContext.destination);

        oscillator.frequency.value = freq;
        oscillator.type = 'sine';

        const startTime = audioContext.currentTime + (index * 0.08);
        gainNode.gain.setValueAtTime(volume * 0.4, startTime);
        gainNode.gain.exponentialRampToValueAtTime(0.01, startTime + 0.15);

        oscillator.start(startTime);
        oscillator.stop(startTime + 0.15);
    });
}

// Coin loss sound (descending notes)
function playCoinLoss(volume) {
    const notes = [783.99, 659.25, 523.25]; // G, E, C (descending)
    notes.forEach((freq, index) => {
        const oscillator = audioContext.createOscillator();
        const gainNode = audioContext.createGain();

        oscillator.connect(gainNode);
        gainNode.connect(audioContext.destination);

        oscillator.frequency.value = freq;
        oscillator.type = 'sine';

        const startTime = audioContext.currentTime + (index * 0.08);
        gainNode.gain.setValueAtTime(volume * 0.4, startTime);
        gainNode.gain.exponentialRampToValueAtTime(0.01, startTime + 0.15);

        oscillator.start(startTime);
        oscillator.stop(startTime + 0.15);
    });
}

// Card flip sound (quick sweep)
function playCardFlip(volume) {
    const oscillator = audioContext.createOscillator();
    const gainNode = audioContext.createGain();

    oscillator.connect(gainNode);
    gainNode.connect(audioContext.destination);

    oscillator.type = 'sawtooth';
    oscillator.frequency.setValueAtTime(200, audioContext.currentTime);
    oscillator.frequency.exponentialRampToValueAtTime(800, audioContext.currentTime + 0.1);

    gainNode.gain.setValueAtTime(volume * 0.3, audioContext.currentTime);
    gainNode.gain.exponentialRampToValueAtTime(0.01, audioContext.currentTime + 0.15);

    oscillator.start(audioContext.currentTime);
    oscillator.stop(audioContext.currentTime + 0.15);
}

// Action success sound (positive chord)
function playActionSuccess(volume) {
    const frequencies = [523.25, 659.25, 783.99]; // C major chord
    frequencies.forEach(freq => {
        const oscillator = audioContext.createOscillator();
        const gainNode = audioContext.createGain();

        oscillator.connect(gainNode);
        gainNode.connect(audioContext.destination);

        oscillator.frequency.value = freq;
        oscillator.type = 'sine';

        gainNode.gain.setValueAtTime(volume * 0.2, audioContext.currentTime);
        gainNode.gain.exponentialRampToValueAtTime(0.01, audioContext.currentTime + 0.3);

        oscillator.start(audioContext.currentTime);
        oscillator.stop(audioContext.currentTime + 0.3);
    });
}

// Action blocked sound (buzzer)
function playActionBlocked(volume) {
    const oscillator = audioContext.createOscillator();
    const gainNode = audioContext.createGain();

    oscillator.connect(gainNode);
    gainNode.connect(audioContext.destination);

    oscillator.frequency.value = 150;
    oscillator.type = 'sawtooth';

    gainNode.gain.setValueAtTime(volume * 0.4, audioContext.currentTime);
    gainNode.gain.exponentialRampToValueAtTime(0.01, audioContext.currentTime + 0.2);

    oscillator.start(audioContext.currentTime);
    oscillator.stop(audioContext.currentTime + 0.2);
}

// Timer warning sound (beep)
function playTimerWarning(volume) {
    const oscillator = audioContext.createOscillator();
    const gainNode = audioContext.createGain();

    oscillator.connect(gainNode);
    gainNode.connect(audioContext.destination);

    oscillator.frequency.value = 1000;
    oscillator.type = 'square';

    gainNode.gain.setValueAtTime(volume * 0.3, audioContext.currentTime);
    gainNode.gain.exponentialRampToValueAtTime(0.01, audioContext.currentTime + 0.1);

    oscillator.start(audioContext.currentTime);
    oscillator.stop(audioContext.currentTime + 0.1);
}

// Influence loss sound (sad trombone effect)
function playInfluenceLoss(volume) {
    const oscillator = audioContext.createOscillator();
    const gainNode = audioContext.createGain();

    oscillator.connect(gainNode);
    gainNode.connect(audioContext.destination);

    oscillator.type = 'sawtooth';
    oscillator.frequency.setValueAtTime(400, audioContext.currentTime);
    oscillator.frequency.exponentialRampToValueAtTime(200, audioContext.currentTime + 0.5);

    gainNode.gain.setValueAtTime(volume * 0.4, audioContext.currentTime);
    gainNode.gain.exponentialRampToValueAtTime(0.01, audioContext.currentTime + 0.5);

    oscillator.start(audioContext.currentTime);
    oscillator.stop(audioContext.currentTime + 0.5);
}

// Challenge sound (dramatic)
function playChallenge(volume) {
    const notes = [392, 440, 494, 523.25]; // G, A, B, C
    notes.forEach((freq, index) => {
        const oscillator = audioContext.createOscillator();
        const gainNode = audioContext.createGain();

        oscillator.connect(gainNode);
        gainNode.connect(audioContext.destination);

        oscillator.frequency.value = freq;
        oscillator.type = 'triangle';

        const startTime = audioContext.currentTime + (index * 0.1);
        gainNode.gain.setValueAtTime(volume * 0.35, startTime);
        gainNode.gain.exponentialRampToValueAtTime(0.01, startTime + 0.2);

        oscillator.start(startTime);
        oscillator.stop(startTime + 0.2);
    });
}

// Make playSound available globally
window.playSound = playSound;
