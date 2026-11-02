#import "ViewController.h"
#import <Metal/Metal.h>
#import <QuartzCore/CAMetalLayer.h>

@interface ViewController ()

@property (nonatomic, strong) CAMetalLayer *metalLayer;

@end

@implementation ViewController

- (void)viewDidLoad {
    [super viewDidLoad];
    
    self.view.backgroundColor = [UIColor blackColor];
    
    // Setup Metal backing layer
    id<MTLDevice> device = MTLCreateSystemDefaultDevice();
    if (device) {
        self.metalLayer = [CAMetalLayer layer];
        self.metalLayer.device = device;
        self.metalLayer.pixelFormat = MTLPixelFormatBGRA8Unorm;
        self.metalLayer.frame = self.view.bounds;
        [self.view.layer addSublayer:self.metalLayer];
    }
}

- (void)viewDidLayoutSubviews {
    [super viewDidLayoutSubviews];
    if (self.metalLayer) {
        self.metalLayer.frame = self.view.bounds;
    }
}

- (BOOL)prefersStatusBarHidden {
    return YES;
}

- (BOOL)prefersHomeIndicatorAutoHidden {
    return YES;
}

- (UIInterfaceOrientationMask)supportedInterfaceOrientations {
    return UIInterfaceOrientationMaskLandscape;
}

- (BOOL)shouldAutorotate {
    return YES;
}

@end
