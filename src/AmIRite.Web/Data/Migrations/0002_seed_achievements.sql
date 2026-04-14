-- Migration 0002: Seed starter achievements

INSERT OR IGNORE INTO achievements (key, name, description, icon, sort_order) VALUES
    ('first_game',      'First Game',      'Complete your first game',                                                     'first_game.png',      1),
    ('perfect_round',   'Perfect Round',   'Guess all questions correctly in a single round',                              'perfect_round.png',   2),
    ('perfect_game',    'Perfect Game',    'Guess all questions correctly across an entire game',                          'perfect_game.png',    3),
    ('ten_games',       'Seasoned Player', 'Complete 10 games',                                                            'ten_games.png',       4),
    ('twenty_five_games','Veteran',        'Complete 25 games',                                                            'twenty_five_games.png',5),
    ('sharp_eye',       'Sharp Eye',       'Achieve 80%+ overall accuracy across any single game',                        'sharp_eye.png',       6),
    ('fooled_them_all', 'Fooled Them All', 'Opponent guesses none of your answers correctly in a single round',           'fooled_them_all.png', 7),
    ('mind_reader',     'Mind Reader',     'Correctly guess opponent''s answer on every question in 5 consecutive rounds', 'mind_reader.png',     8);
