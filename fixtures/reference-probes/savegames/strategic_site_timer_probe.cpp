// The timer/follower and score statements of GeoscapeState::processMissionSite are inserted verbatim.
#include <iostream>
#include <string>
#include <vector>

struct Rule {
    bool force;
    bool despawnEvenIfTargeted() const { return force; }
};
struct Deployment {
    int points, penalty;
    int getPoints() const { return points; }
    int getDespawnPenalty() const { return penalty; }
};
struct Craft {
    bool returned = false;
    void returnToBase() { returned = true; }
};
struct Site {
    int remaining;
    Rule* rule;
    Deployment* deployment;
    std::vector<Craft*> followers;
    int getSecondsRemaining() const { return remaining; }
    void setSecondsRemaining(int value) { remaining = value; }
    Rule* getRules() const { return rule; }
    Deployment* getDeployment() const { return deployment; }
    std::vector<Craft*>* getFollowers() { return &followers; }
    std::vector<Craft*> getCraftFollowers() { return followers; }
    std::string getName(const std::string&) const { return "Site"; }
};
struct Game {
    std::string getLanguage() const { return "en-US"; }
};
struct UfoLostState {
    explicit UfoLostState(const std::string&) {}
};
class GeoscapeState {
    Game* _game;
public:
    explicit GeoscapeState(Game* game) : _game(game) {}
    int popups = 0;
    void popup(UfoLostState* state) { ++popups; delete state; }
    std::pair<bool, int> run(Site* site) {
        // TIMER_AND_FOLLOWERS
        // SCORE
        return {removeSite, score};
    }
};
struct Case {
    int seconds;
    bool follower, force;
    int points, penalty;
};
int main() {
    std::vector<Case> cases = {
        {3600, false, false, 4, 9},
        {1800, false, false, 4, 9},
        {1799, false, false, 4, 9},
        {0, true, false, 4, 9},
        {0, true, true, 4, 9}
    };
    std::cout << std::boolalpha << "{\"rows\":[";
    bool first = true;
    for (const auto& input : cases) {
        if (!first) std::cout << ',';
        first = false;
        Rule rule{input.force};
        Deployment deployment{input.points, input.penalty};
        Craft craft;
        Site site{input.seconds, &rule, &deployment};
        if (input.follower) site.followers.push_back(&craft);
        Game game;
        GeoscapeState geoscape(&game);
        auto [removed, score] = geoscape.run(&site);
        std::cout << '[' << input.seconds << ',' << input.follower << ',' << input.force << ','
                  << input.points << ',' << input.penalty << ',' << removed << ',' << site.remaining
                  << ',' << score << ',' << craft.returned << ',' << geoscape.popups << ']';
    }
    std::cout << "]}\n";
}
