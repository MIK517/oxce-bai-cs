// The capture script inserts the pinned engine's unmodified dispatch loop.
// GUI/timer collaborators are replaced by trace collectors; handler internals are
// deliberately outside this probe. No original assets are required.
#include <iostream>
#include <vector>
#define FALLTHROUGH [[fallthrough]]
enum TimeTrigger { TIME_5SEC, TIME_10MIN, TIME_30MIN, TIME_1HOUR, TIME_1DAY, TIME_1MONTH };
struct Clock {
    int ticks = 0;
    TimeTrigger first;
    TimeTrigger advance() { return ticks++ == 0 ? first : TIME_5SEC; }
};
struct Save { Clock clock; Clock* getTime() { return &clock; } };
struct Game { Save save; Save* getSavedGame() { return &save; } };
struct Probe {
    Game game;
    Game* _game = &game;
    bool _pause = false;
    int pauseAt;
    std::vector<int> trace;
    void record(int trigger) { trace.push_back(trigger); if (trigger == pauseAt) _pause = true; }
    void time1Month() { record(5); }
    void time1Day() { record(4); }
    void time1Hour() { record(3); }
    void time30Minutes() { record(2); }
    void time10Minutes() { record(1); }
    void time5Seconds() { record(0); }
    void run(int timeSpan) {
// DISPATCH_LOOP
    }
};
int main() {
    std::cout << "{\"cases\":[";
    bool first = true;
    for (int trigger = 0; trigger <= 5; ++trigger) {
        for (int pauseAt : {-1, 3, 5}) {
            Probe p;
            p.game.save.clock.first = static_cast<TimeTrigger>(trigger);
            p.pauseAt = pauseAt;
            p.run(2);
            if (!first) std::cout << ',';
            first = false;
            std::cout << "{\"trigger\":" << trigger << ",\"pauseAt\":" << pauseAt
                << ",\"ticks\":" << p.game.save.clock.ticks << ",\"trace\":[";
            for (size_t i = 0; i < p.trace.size(); ++i) {
                if (i) std::cout << ',';
                std::cout << p.trace[i];
            }
            std::cout << "]}";
        }
    }
    std::cout << "]}\n";
}
