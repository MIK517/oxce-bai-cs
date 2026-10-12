// Landing-site shade probe. The capture script inserts the reference Cord, GlobeStaticData,
// CreateShadow::getShadowValue, Globe::getSunDirection, GameTime::getDaylight and the shade part of
// Globe::getPolygonTextureAndShade verbatim; only the clock, the option and RNG::RandomState are stubs.
#define _USE_MATH_DEFINES
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <algorithm>

typedef int16_t Sint16;
typedef uint8_t Uint8;

static inline bool AreSame(double a, double b) { return a == b; }

template<typename T> static inline T Clamp(const T& x, const T& min, const T& max) { return std::min(std::max(x, min), max); }

// CORD_HEADER

namespace OpenXcom
{

namespace Options { bool globeSeasons = false; }

namespace RNG
{
// The noise table is never read: the landing shade uses noise 0.
struct RandomState
{
    int generate(int min, int) { return min; }
};
}

class GameTime
{
public:
    int _second, _minute, _hour, _weekday, _day, _month, _year;
    int getSecond() const { return _second; }
    int getMinute() const { return _minute; }
    int getHour() const { return _hour; }
    int getDay() const { return _day; }
    int getMonth() const { return _month; }
    int getYear() const { return _year; }
    double getDaylight() const;
};

// GAMETIME_DAYLIGHT

struct SavedGame { GameTime time; GameTime* getTime() { return &time; } };
struct Game { SavedGame saved; SavedGame* getSavedGame() { return &saved; } };

namespace
{
// GLOBE_STATIC_DATA

struct CreateShadow
{
// CREATE_SHADOW_VALUE
};
}

class Globe
{
public:
    Game* _game;
    Cord getSunDirection(double lon, double lat) const;
    int shade(double lon, double lat) const;
};

// GLOBE_SUN_DIRECTION

int Globe::shade(double lon, double lat) const
{
    int s = 0;
    int* shade = &s;
// GLOBE_SHADE
    return s;
}

}

using namespace OpenXcom;

int main()
{
    Game game{};
    Globe globe{&game};
    const double longitudes[] = {0.0, 0.17453292519943295, 1.0, 2.5, 3.141592653589793, 4.0, 5.5, 6.2};
    const double latitudes[] = {-1.4, -0.7, -0.2, 0.0, 0.3, 0.9, 1.3};
    const int dates[][3] = {{1999, 1, 1}, {1999, 3, 20}, {1999, 6, 21}, {2000, 2, 29}, {2000, 12, 31}, {2100, 9, 23}};
    const int clocks[][3] = {{0, 0, 0}, {5, 30, 0}, {6, 0, 5}, {11, 59, 55}, {12, 0, 0}, {17, 45, 10}, {18, 0, 0}, {23, 59, 55}};
    std::printf("{\"rows\":[");
    bool first = true;
    for (int seasons = 0; seasons < 2; ++seasons)
    {
        Options::globeSeasons = seasons != 0;
        for (const auto& date : dates)
            for (const auto& clock : clocks)
                for (double lon : longitudes)
                    for (double lat : latitudes)
                    {
                        GameTime& t = game.saved.time;
                        t._year = date[0]; t._month = date[1]; t._day = date[2];
                        t._hour = clock[0]; t._minute = clock[1]; t._second = clock[2];
                        std::printf("%s[%d,%d,%d,%d,%d,%d,%d,%.17g,%.17g,%d]", first ? "" : ",", seasons, date[0], date[1], date[2],
                            clock[0], clock[1], clock[2], lon, lat, globe.shade(lon, lat));
                        first = false;
                    }
    }
    std::printf("]}\n");
    return 0;
}
